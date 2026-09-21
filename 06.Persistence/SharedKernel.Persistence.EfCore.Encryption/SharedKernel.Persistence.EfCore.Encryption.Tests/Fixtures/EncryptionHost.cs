using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel.Application.Context;
using SharedKernel.Cryptography.Envelope;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Encryption.BlindIndex;
using SharedKernel.Persistence.EfCore.Encryption.Extensions;
using SharedKernel.Persistence.EfCore.Extensions;
using SharedKernel.Persistence.EfCore.MultiTenancy;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.EfCore.Encryption.Tests.Fixtures;

/// <summary>Deterministic test key material.</summary>
public static class TestKeys
{
    public static readonly byte[] V1 = Enumerable.Repeat((byte)0x11, 32).ToArray();
    public static readonly byte[] V2 = Enumerable.Repeat((byte)0x22, 32).ToArray();
    public static readonly string BlindV1 = Convert.ToBase64String(Enumerable.Repeat((byte)0x33, 32).ToArray());
    public static readonly string BlindV2 = Convert.ToBase64String(Enumerable.Repeat((byte)0x44, 32).ToArray());

    public static StaticEncryptionKeyProvider Provider(string currentKeyId = "v1") =>
        new(currentKeyId, [new CryptographicKey("v1", V1), new CryptographicKey("v2", V2)]);
}

/// <summary>A caller identity whose tenant a test can change.</summary>
public sealed class TestRequestContext : IRequestContext
{
    public Guid? TenantId { get; set; }

    public bool IsAuthenticated => true;

    public string? UserId => "test-user";

    public ValueTask<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken) => ValueTask.FromResult(true);
}

public sealed class IbanNormalizer : IBlindIndexNormalizer
{
    public const string NormalizerName = "iban";

    public string Name => NormalizerName;

    public string Normalize(string value) => value.ToUpperInvariant();
}

/// <summary>An envelope provider whose master key is a local AES key, counting unwrap calls.</summary>
public sealed class TestEnvelopeProvider : IEnvelopeEncryptionProvider
{
    private static readonly byte[] MasterKey = Enumerable.Repeat((byte)0x55, 32).ToArray();

    public int Unwraps { get; private set; }

    public ValueTask<EnvelopeDataKey> GenerateDataKeyAsync(CancellationToken cancellationToken = default)
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var wrapped = new byte[12 + 16 + 32];
        using (var aes = new AesGcm(MasterKey, 16))
            aes.Encrypt(nonce, key, wrapped.AsSpan(28), wrapped.AsSpan(12, 16));
        nonce.CopyTo(wrapped, 0);
        return new ValueTask<EnvelopeDataKey>(new EnvelopeDataKey(key, wrapped, "master-1"));
    }

    public ValueTask<Result<byte[]>> UnwrapDataKeyAsync(ReadOnlyMemory<byte> wrappedKey, string masterKeyId, CancellationToken cancellationToken = default)
    {
        Unwraps++;
        var wrapped = wrappedKey.Span;
        var key = new byte[32];
        using (var aes = new AesGcm(MasterKey, 16))
            aes.Decrypt(wrapped[..12], wrapped[28..], wrapped.Slice(12, 16), key);
        return ValueTask.FromResult<Result<byte[]>>(key);
    }
}

/// <summary>Builds a DI container that wires a context the way a service does, so the real registration runs.</summary>
public static class EncryptionHost
{
    public static ServiceProvider Build<TContext>(
        string connectionString,
        TestRequestContext? requestContext = null,
        string currentKeyId = "v1",
        Action<FieldEncryptionBuilder>? configure = null,
        Action<IServiceCollection>? configureServices = null,
        bool wireEncryption = true,
        string blindIndexVersion = "v1")
        where TContext : SharedKernelDbContext
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IRequestContext>(requestContext ?? new TestRequestContext());

        // The key source is registered once, as the async provider only: no sync/async double registration.
        services.AddSingleton<IEncryptionKeyProvider>(TestKeys.Provider(currentKeyId));
        // A TenantedDbContext gets the tenant write guard without UseMultiTenancy(); that call only adds RLS.
        services.AddSharedKernelPostgres<TContext>(new ConfigurationBuilder().Build(), "encryption", builder =>
        {
            builder
                .UseDataSource(TestNpgsqlDataSources.Get(connectionString))
                .ConfigureProvider(o => o.MaxRetryCount = 0)
                .ConfigureDbContext((_, options) => options.ConfigureWarnings(
                    w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)));

            if (wireEncryption)
            {
                builder.UseFieldEncryption(k =>
                {
                    k.AddBlindIndexNormalizer<IbanNormalizer>();
                    k.Configure(o =>
                    {
                        o.BlindIndexKeys.CurrentVersion = blindIndexVersion;
                        o.BlindIndexKeys.Keys["v1"] = TestKeys.BlindV1;
                        if (blindIndexVersion == "v2")
                            o.BlindIndexKeys.Keys["v2"] = TestKeys.BlindV2;
                    });
                    configure?.Invoke(k);
                });
            }

            // After UseFieldEncryption, so a test extension registered here is applied after encryption.
            configureServices?.Invoke(services);
        });

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    public static string Database(string connectionString, string database) =>
        new NpgsqlConnectionStringBuilder(connectionString) { Database = database }.ConnectionString;

    public static async Task<List<Dictionary<string, object?>>> QueryAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }

        return rows;
    }

    public static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public static async Task<TContext> CreateDatabaseAsync<TContext>(IServiceScope scope)
        where TContext : DbContext
    {
        var context = scope.ServiceProvider.GetRequiredService<TContext>();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();
        return context;
    }
}
