using SharedKernel.Application.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Persistence.EfCore.Encryption.Extensions;
using SharedKernel.Persistence.EfCore.Encryption.Tests.TestFixtures;
using SharedKernel.Persistence.EfCore.Extensions;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.EfCore.Encryption.Tests.Integration;

/// <summary>
/// Proves the model-build-time guard added to <c>EncryptionModelConvention.ValidateRotationKeyShape</c> fires for
/// a same-table owned entity type's <c>.Encrypt(...)</c> property — fast, at startup, with a clear message —
/// instead of the confusing "no backing field" <see cref="InvalidOperationException"/> EF Core itself throws the
/// first time such a property is materialized.
/// </summary>
/// <remarks>
/// A same-table owned entity type's primary key is ALWAYS a shadow property in EF Core 10 (it mirrors the
/// owner's key with no backing CLR field or auto-property), so <c>AssociatedDataBuilder</c> cannot read it from a
/// bare materialized instance the way it reads a genuine CLR-backed key. This is a real, discovered architectural
/// limitation, not a configuration mistake this fixture happens to make: no way to pin a same-table owned type's
/// key onto a real CLR property exists in EF Core 10. The only two fixes are the ones this exception's message
/// names — move the encrypted property to the OWNING entity type, or promote the owned type to a genuine
/// (non-shadow-keyed) shape, such as a complex type (see <c>EncCustomer.BillingAddress</c> and
/// <c>EncryptionRotationIntegrationTests.Rotate_ComplexTypeEncryptedProperty_...</c> for that alternative, which
/// this same guard's key-shape validation lets through because a complex type reuses the OWNING entity's own,
/// genuinely CLR-backed primary key).
/// </remarks>
[Collection("EncryptionPostgres")]
public sealed class EncryptionOwnedTypeShadowKeyGuardTests
{
    private readonly PostgreSqlContainerFixture _fixture;

    public EncryptionOwnedTypeShadowKeyGuardTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    private string ConnectionString(string database) =>
        new NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { Database = database }.ConnectionString;

    [Fact]
    public async Task EncryptOnSameTableOwnedEntityProperty_ThrowsAtModelBuild_NotAtRuntime()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var actor = new FakeAuditActorContext();
        services.AddSingleton(actor);
        services.AddSingleton<SharedKernel.Application.Context.IRequestContext>(actor);
        services.AddSingleton<SharedKernel.Application.Context.IRequestContext>(actor);

        var keyProvider = new StaticEncryptionKeyProvider("v1", [new CryptographicKey("v1", Enumerable.Repeat((byte)0x33, 32).ToArray())]);
        services.AddSingleton(keyProvider);
        services.AddSingleton<IEncryptionKeyProvider>(sp => sp.GetRequiredService<StaticEncryptionKeyProvider>());
        services.AddSingleton<ISynchronousEncryptionKeyProvider>(sp => sp.GetRequiredService<StaticEncryptionKeyProvider>());

        services.AddSharedKernelPostgres<ShadowKeyGuardDbContext>(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), "encryption-tests", p => p
            .UseDataSource(TestNpgsqlDataSources.Get(ConnectionString("sk_enc_shadow_key_guard")))
            .ConfigureDbContext((_, options) => options.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)))
            .UseMultiTenancy()
            .WithEncryption());

        await using var sp = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = sp.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ShadowKeyGuardDbContext>();

        // Model building alone must fail — no SaveChanges, no query, not even a database connection.
        var act = () => context.Model.GetEntityTypes().ToList();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*shadow property*")
                .Where(ex => ex.Message.Contains("ShadowKeyOwnedValue", StringComparison.Ordinal));
    }
}
