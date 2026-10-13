using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Core.Exceptions;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Persistence.Abstractions.Diagnostics;
using SharedKernel.Persistence.Abstractions.Errors;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Telemetry;

namespace SharedKernel.Persistence.EfCore.Tests.Interceptors;

/// <summary>
/// A write the tenant guard rejects raises the classified exception and counts one
/// <c>sharedkernel.persistence.tenant_isolation_violations</c>, tagged with the aggregate type and nothing else.
/// </summary>
public sealed class TenantIsolationTelemetryTests
{
    private const string Instrument = "sharedkernel.persistence.tenant_isolation_violations";

    private static TenantedTestDbContext CreateContext(SqliteConnection connection, TenantId tenantId)
    {
        var options = new DbContextOptionsBuilder<TenantedTestDbContext>()
            .UseSqlite(connection)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
        var actor = TestDbContextFactory.CreateAuthenticatedActorContext(Guid.NewGuid(), tenantId);
        var ctx = new TenantedTestDbContext(options, PersistenceContextDependencies.Create(actor, new SystemClock()));
        ctx.RefreshRequestContext(actor);
        return ctx;
    }

    [Fact]
    public async Task AWriteToAnotherTenant_IsRejected_AndCountedByAggregateType()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var tenant = new TenantId(Guid.NewGuid());
        using var ctx = CreateContext(connection, tenant);
        await ctx.Database.EnsureCreatedAsync();
        using var metrics = MetricRecorder.StartRecording(PersistenceTelemetry.Name);

        ctx.TenantedAggregates.Add(new TenantedTestAggregate(TenantedTestId.New(), "Foreign", new TenantId(Guid.NewGuid()), new SystemClock()));
        var act = () => ctx.SaveChangesAsync();

        var thrown = (await act.Should().ThrowAsync<ForbiddenException>()).Which;
        thrown.Error.Code.Should().Be(PersistenceErrorCodes.Tenancy.TenantIsolationViolation);
        metrics
            .ShouldHaveInstrument(Instrument, unit: "{violation}", kind: InstrumentKind.Counter)
            .ShouldHaveMeasurement(Instrument, 1, new KeyValuePair<string, object?>("persistence.aggregate_type", nameof(TenantedTestAggregate)))
            .ShouldHaveTagSet(Instrument, "persistence.aggregate_type")
            .ShouldFollowMetricConvention();
    }
}
