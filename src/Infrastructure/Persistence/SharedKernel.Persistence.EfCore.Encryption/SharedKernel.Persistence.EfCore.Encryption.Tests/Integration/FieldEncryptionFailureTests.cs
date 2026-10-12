using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Cryptography.Envelope;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Persistence.EfCore.Encryption.Diagnostics;
using SharedKernel.Persistence.EfCore.Encryption.Tests.Fixtures;
using SharedKernel.Persistence.EfCore.Encryption.TenantKeys;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Telemetry;

namespace SharedKernel.Persistence.EfCore.Encryption.Tests.Integration;

/// <summary>An envelope provider that cannot reach its master key: the failure carries distinctive text and a URL.</summary>
public sealed class UnavailableUnwrapProvider : IEnvelopeEncryptionProvider
{
    public const string Leak = "https://vault.example.net/keys/master-secret-42 refused the request";

    public ValueTask<EnvelopeDataKey> GenerateDataKeyAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public ValueTask<Result<byte[]>> UnwrapDataKeyAsync(ReadOnlyMemory<byte> wrappedKey, string masterKeyId, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Result<byte[]>.Failure(Error.Unavailable("vault.unreachable", Leak)));
}

/// <summary>An envelope provider whose unwrap times out.</summary>
public sealed class TimeoutUnwrapProvider : IEnvelopeEncryptionProvider
{
    public ValueTask<EnvelopeDataKey> GenerateDataKeyAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public ValueTask<Result<byte[]>> UnwrapDataKeyAsync(ReadOnlyMemory<byte> wrappedKey, string masterKeyId, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Result<byte[]>.Failure(Error.Timeout("vault.timeout", UnavailableUnwrapProvider.Leak)));
}

/// <summary>An envelope provider whose unwrap fails for an unclassified reason.</summary>
public sealed class UnexpectedUnwrapProvider : IEnvelopeEncryptionProvider
{
    public ValueTask<EnvelopeDataKey> GenerateDataKeyAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public ValueTask<Result<byte[]>> UnwrapDataKeyAsync(ReadOnlyMemory<byte> wrappedKey, string masterKeyId, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Result<byte[]>.Failure(Error.Validation("vault.bad_key", UnavailableUnwrapProvider.Leak)));
}

/// <summary>An envelope provider that generates a data key shorter than the minimum.</summary>
public sealed class ShortKeyEnvelopeProvider : IEnvelopeEncryptionProvider
{
    public ValueTask<EnvelopeDataKey> GenerateDataKeyAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new EnvelopeDataKey(new byte[8], new byte[16], "https://vault.example.net/keys/master-secret-42"));

    public ValueTask<Result<byte[]>> UnwrapDataKeyAsync(ReadOnlyMemory<byte> wrappedKey, string masterKeyId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

/// <summary>
/// Decrypt and data-key faults surface as the classified <see cref="FieldEncryptionException"/> with fixed, client-safe
/// codes and messages: never a raw <c>CryptographicException</c>, never provider text or a master key id.
/// </summary>
[Collection("EncryptionPostgres")]
public sealed partial class FieldEncryptionFailureTests(PostgreSqlContainerFixture fixture)
{
    private readonly TenantId _tenant = new(Guid.NewGuid());

    private string Cs(string db) => EncryptionHost.Database(fixture.ConnectionString, db);

    private static Customer NewCustomer(TenantId tenant) => new()
    {
        TenantId = tenant,
        Name = "Ada",
        Email = "ada@example.com",
        Note = "vip",
        Billing = new Address { City = "Ankara", Bank = new BankAccount { Iban = "tr33 0006 1005", BankName = "Z" } },
    };

    private async Task<string> SeedAsync(string database)
    {
        var cs = Cs(database);
        await using var sp = EncryptionHost.Build<CustomerDbContext>(cs, new TestRequestContext { TenantId = _tenant });
        await using var scope = sp.CreateAsyncScope();
        var context = await EncryptionHost.CreateDatabaseAsync<CustomerDbContext>(scope);
        context.Customers.AddRange(NewCustomer(_tenant), NewCustomer(_tenant));
        await context.SaveChangesAsync();
        return cs;
    }

    private async Task<FieldEncryptionException> ReadFailureAsync(string cs, Action<FieldEncryptionBuilder>? configure = null)
    {
        await using var sp = EncryptionHost.Build<CustomerDbContext>(cs, new TestRequestContext { TenantId = _tenant }, configure: configure);
        await using var scope = sp.CreateAsyncScope();
        var act = () => scope.ServiceProvider.GetRequiredService<CustomerDbContext>().Customers.AsNoTracking().ToListAsync();
        return (await act.Should().ThrowAsync<FieldEncryptionException>()).Which;
    }

    [Fact]
    public async Task MalformedPayload_ThrowsTheClassifiedException_WithTheRemedy_AndCountsTheSameReason()
    {
        var cs = await SeedAsync("enc_f_malformed");
        await EncryptionHost.ExecuteAsync(cs, "UPDATE customers SET note = 'planted'");
        using var metrics = MetricRecorder.StartRecording(EncryptionMeter.Name);

        var thrown = await ReadFailureAsync(cs);

        thrown.Error.Code.Should().Be("persistence.encryption.malformed_payload");
        thrown.Error.Type.Should().Be(ErrorType.Unexpected);
        thrown.Error.Message.Should().Be("A stored encrypted value is not a valid encrypted payload.");
        thrown.Message.Should().Contain("Customer.Note").And.Contain("EncryptPlaintext").And.NotContain("planted");
        metrics.Count("persistence.encryption.decrypt_failures", new KeyValuePair<string, object?>("reason", "malformed_payload"))
            .Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CiphertextCopiedFromAnotherRow_ThrowsDecryptionFailed_NamingNoKey()
    {
        var cs = await SeedAsync("enc_f_copied");
        await EncryptionHost.ExecuteAsync(cs,
            "UPDATE customers SET email = (SELECT email FROM customers ORDER BY id LIMIT 1) WHERE id = (SELECT id FROM customers ORDER BY id DESC LIMIT 1)");
        using var metrics = MetricRecorder.StartRecording(EncryptionMeter.Name);

        var thrown = await ReadFailureAsync(cs);

        thrown.Error.Code.Should().Be("persistence.encryption.decryption_failed");
        thrown.Error.Type.Should().Be(ErrorType.Unexpected);
        thrown.Error.Message.Should().Be("A stored encrypted value could not be decrypted.");
        thrown.Message.Should().Contain("Customer.Email").And.NotContain("'v1'");
        thrown.Should().NotBeAssignableTo<System.Security.Cryptography.CryptographicException>();
        metrics.Count("persistence.encryption.decrypt_failures", new KeyValuePair<string, object?>("reason", "decryption_failed"))
            .Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ShortDataKey_FromTheProvider_ThrowsDataKeyInvalid_WithoutTheMasterKeyId()
    {
        var cs = Cs("enc_f_shortkey");
        var request = new TestRequestContext { TenantId = _tenant };
        await using var sp = EncryptionHost.Build<CustomerDbContext>(cs, request, configure: k => k.UseTenantDataKeys<ShortKeyEnvelopeProvider>());
        await using var scope = sp.CreateAsyncScope();
        var context = await EncryptionHost.CreateDatabaseAsync<CustomerDbContext>(scope);
        await EncryptionHost.ExecuteAsync(cs, TenantKeyStore.CreateTableSql(schema: null));
        context.Customers.Add(NewCustomer(_tenant));

        var act = () => context.SaveChangesAsync();

        var thrown = (await act.Should().ThrowAsync<FieldEncryptionException>()).Which;
        thrown.Error.Code.Should().Be("persistence.encryption.data_key_invalid");
        thrown.Error.Type.Should().Be(ErrorType.Unexpected);
        thrown.Message.Should().NotContain("vault.example.net").And.NotContain("master-secret-42");
        thrown.Error.Message.Should().NotContain("vault.example.net");
    }

    [Theory]
    [InlineData(typeof(UnavailableUnwrapProvider), ErrorType.Unavailable)]
    [InlineData(typeof(TimeoutUnwrapProvider), ErrorType.Timeout)]
    [InlineData(typeof(UnexpectedUnwrapProvider), ErrorType.Unexpected)]
    public async Task UnwrapFailure_KeepsOnlyTheProvidersErrorType_NeverItsTextOrTheMasterKeyId(Type providerType, ErrorType expected)
    {
        var cs = Cs("enc_f_unwrap_" + providerType.Name.ToLowerInvariant());
        var request = new TestRequestContext { TenantId = _tenant };
        await using (var seeding = EncryptionHost.Build<CustomerDbContext>(cs, request, configure: k => k.UseTenantDataKeys<TestEnvelopeProvider>()))
        await using (var scope = seeding.CreateAsyncScope())
        {
            var context = await EncryptionHost.CreateDatabaseAsync<CustomerDbContext>(scope);
            await EncryptionHost.ExecuteAsync(cs, TenantKeyStore.CreateTableSql(schema: null));
            context.Customers.Add(NewCustomer(_tenant));
            await context.SaveChangesAsync();
        }

        var thrown = await ReadFailureAsync(cs, k => UseProvider(k, providerType));

        thrown.Error.Code.Should().Be("persistence.encryption.data_key_unwrap_failed");
        thrown.Error.Type.Should().Be(expected);
        string[] forbidden = ["vault.example.net", "master-secret-42", "master-1", "refused the request"];
        foreach (var text in forbidden)
        {
            thrown.Message.Should().NotContain(text);
            thrown.Error.Message.Should().NotContain(text);
        }
    }

    private static void UseProvider(FieldEncryptionBuilder builder, Type providerType)
    {
        _ = providerType == typeof(UnavailableUnwrapProvider) ? builder.UseTenantDataKeys<UnavailableUnwrapProvider>()
            : providerType == typeof(TimeoutUnwrapProvider) ? builder.UseTenantDataKeys<TimeoutUnwrapProvider>()
            : builder.UseTenantDataKeys<UnexpectedUnwrapProvider>();
    }

    [GeneratedRegex(@"\{@?(?<name>[A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex Placeholder();

    [Fact]
    public void NoLoggerMessageTemplate_UsesABannedPlaceholder()
    {
        string[] banned = ["ActorId", "ActorTenantId", "ChainTenant", "TenantId", "IdempotencyKey", "ExceptionType", "MasterKeyId", "CorrelationId"];
        var violations = new List<string>();

        foreach (var type in typeof(FieldEncryptionException).Assembly.GetTypes())
        {
            foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (method.GetCustomAttribute<LoggerMessageAttribute>()?.Message is not { } template)
                    continue;

                foreach (Match match in Placeholder().Matches(template))
                {
                    if (banned.Contains(match.Groups["name"].Value, StringComparer.OrdinalIgnoreCase))
                        violations.Add($"{type.Name}.{method.Name}: {{{match.Groups["name"].Value}}}");
                }
            }
        }

        violations.Should().BeEmpty();
    }
}
