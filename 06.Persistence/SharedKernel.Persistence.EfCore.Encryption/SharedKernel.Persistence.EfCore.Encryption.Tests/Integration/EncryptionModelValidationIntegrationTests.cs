using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel.Persistence.EfCore.Encryption.Tests.TestFixtures;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.EfCore.Encryption.Tests.Integration;

/// <summary>Model-build-time guards: purpose uniqueness, and the blind-index shadow column's physical shape.</summary>
[Collection("EncryptionPostgres")]
public sealed class EncryptionModelValidationIntegrationTests
{
    private readonly PostgreSqlContainerFixture _fixture;

    public EncryptionModelValidationIntegrationTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    private string ConnectionString(string database) =>
        new NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { Database = database }.ConnectionString;

    [Fact]
    public async Task TwoEncryptedPropertiesSharingOnePurpose_ThrowsAtModelBuild()
    {
        // Never touches the database — EncryptionModelConvention runs the first time the model is finalized,
        // which happens on first access to context.Model, before any query.
        await using var sp = EncryptionTestHost.Build<DuplicatePurposeDbContext>(
            ConnectionString("sk_enc_duplicate_purpose"), actorContext: new FakeAuditActorContext());
        await using var scope = sp.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DuplicatePurposeDbContext>();

        var act = () => context.Model.GetEntityTypes().ToList();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*First*")
            .Where(ex => ex.Message.Contains("Second", StringComparison.Ordinal) && ex.Message.Contains("duplicate.shared", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BlindIndexShadowColumn_HasBoundedMaxLengthAndAnIndex()
    {
        // Never touches the database either — inspects the finalized model's metadata directly.
        await using var sp = EncryptionTestHost.Build(ConnectionString("sk_enc_blind_index_shape"), actorContext: new FakeAuditActorContext());
        await using var scope = sp.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<EncryptionTestDbContext>();

        var entityType = context.Model.FindEntityType(typeof(EncCustomer))!;
        var shadowProperty = entityType.FindProperty("EmailBlindIndex");

        shadowProperty.Should().NotBeNull();
        // The blind index is always a 64-character lowercase-hex HMAC-SHA256 digest — leaving the column unbounded
        // `text` both defeats early validation and (via the index below) wastes space; every WhereBlindIndexEquals
        // lookup must be an actual index seek, not a sequential scan.
        shadowProperty!.GetMaxLength().Should().Be(64);
        entityType.GetIndexes().Should().Contain(i => i.Properties.Count == 1 && i.Properties[0] == shadowProperty);
    }
}
