using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Core.Exceptions;
using SharedKernel.Execution.Auditing;
using SharedKernel.Execution.Context;
using SharedKernel.Persistence.Abstractions.Errors;
using SharedKernel.Persistence.EfCore.Auditing.Tests.Support;
using SharedKernel.Primitives.Errors;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Logging;

namespace SharedKernel.Persistence.EfCore.Auditing.Tests.Integration;

/// <summary>
/// Classified, leak-free failures of the audit services: one "no tenant" behaviour, one not-found answer for a missing and
/// a foreign record, no key or id in an idempotency conflict, and no tenant, actor or key in a log template.
/// </summary>
[Collection("AuditPostgres")]
public sealed partial class ErrorSafetyTests(PostgreSqlContainerFixture fixture)
{
    private static AuditEntry Entry(string resourceId = "order-1", string? idempotencyKey = null) => new()
    {
        Action = "OrderRejected",
        ResourceType = "Order",
        ResourceId = resourceId,
        Outcome = AuditOutcome.Failed,
        AfterSnapshot = "{\"status\":\"rejected\"}",
        IdempotencyKey = idempotencyKey,
    };

    private static async Task ShouldRequireATenantAsync(Func<Task> act)
    {
        var thrown = (await act.Should().ThrowAsync<ForbiddenException>()).Which;
        thrown.Error.Should().Be(PersistenceSharedErrors.TenantRequired());
        thrown.Error.Code.Should().Be("persistence.tenant_required");
        thrown.Error.Type.Should().Be(ErrorType.Forbidden);
    }

    [Fact]
    public async Task NoTenant_AppendQueryExportVerifyCheckpointAndErase_AllAnswerTheSharedForbiddenError()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await using var host = LedgerHost.Build(cs);
        host.Context.TenantId = null;

        await ShouldRequireATenantAsync(() => host.InScopeAsync(sp => sp.GetRequiredService<EfAuditTrailWriter>().RecordAsync(Entry())));
        await ShouldRequireATenantAsync(() => host.InScopeAsync(sp => sp.GetRequiredService<IAuditQueryService>()
            .QueryAsync(new AuditRecordQuery { ResourceType = "Order" })));
        await ShouldRequireATenantAsync(() => host.InScopeAsync(sp => sp.GetRequiredService<IAuditQueryService>().VerifyChainAsync("Order")));
        await ShouldRequireATenantAsync(() => host.InScopeAsync(async sp =>
        {
            await foreach (var _ in sp.GetRequiredService<IAuditQueryService>()
                .ExportRangeAsync("Order", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow))
            {
            }

            return 0;
        }));
        await ShouldRequireATenantAsync(() => host.InScopeAsync(sp => sp.GetRequiredService<IAuditCheckpointService>().CreateCheckpointAsync("Order")));
        await ShouldRequireATenantAsync(() => host.InScopeAsync(sp => sp.GetRequiredService<IAuditLedgerMaintenance>()
            .EraseResourcePayloadsAsync("Order", "order-1", "x")));
    }

    [Fact]
    public async Task NoTenant_ASystemIdentityOrACrossTenantScope_StillReachTheSystemChain()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await using var host = LedgerHost.Build(cs);
        host.Context.TenantId = null;

        host.Context.ActorKind = ActorKind.System;
        var system = await host.InScopeAsync(sp => sp.GetRequiredService<EfAuditTrailWriter>().RecordAsync(Entry()));
        system.TenantId.Should().BeNull();

        host.Context.ActorKind = ActorKind.User;
        using (host.Scope.Enter("audit test"))
        {
            var scoped = await host.InScopeAsync(sp => sp.GetRequiredService<EfAuditTrailWriter>().RecordAsync(Entry("order-2")));
            scoped.TenantId.Should().BeNull();
        }
    }

    [Fact]
    public async Task AChainOfAnotherTenant_IsForbidden_WithNoTenantInTheText()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await using var host = LedgerHost.Build(cs);
        host.Context.TenantId = TestRequestContext.TenantB;

        var act = () => host.InScopeAsync(sp => sp.GetRequiredService<IAuditCheckpointService>()
            .CreateCheckpointForChainAsync(TestRequestContext.TenantA, "Order"));

        var thrown = (await act.Should().ThrowAsync<ForbiddenException>()).Which;
        thrown.Error.Code.Should().Be(PersistenceErrorCodes.TenantIsolationViolation);
        thrown.Error.Type.Should().Be(ErrorType.Forbidden);
        thrown.Message.Should().NotContain(TestRequestContext.TenantA.ToString()).And.NotContain(TestRequestContext.TenantB.ToString());
        thrown.Error.Message.Should().NotContain(TestRequestContext.TenantA.ToString());
    }

    [Fact]
    public async Task ErasePayload_OfAMissingRecord_AndOfAForeignRecord_AreIndistinguishable()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await using var host = LedgerHost.Build(cs);
        var foreign = await host.InScopeAsync(sp => sp.GetRequiredService<EfAuditTrailWriter>().RecordAsync(Entry()));
        host.Context.TenantId = TestRequestContext.TenantB;

        async Task<NotFoundException> AttemptAsync(Guid id) =>
            (await FluentActions.Awaiting(() => host.InScopeAsync(sp => sp.GetRequiredService<IAuditLedgerMaintenance>()
                .ErasePayloadAsync(id, "x"))).Should().ThrowAsync<NotFoundException>()).Which;

        var missingId = Guid.NewGuid();
        var missing = await AttemptAsync(missingId);
        var other = await AttemptAsync(foreign.Id);

        other.Error.Should().Be(missing.Error);
        other.Error.Message.Should().Be(missing.Error.Message);
        other.Message.Should().Be(missing.Message);
        missing.Error.Code.Should().Be("persistence.audit_record_not_found");
        missing.Error.Type.Should().Be(ErrorType.NotFound);
        missing.Message.Should().NotContain(missingId.ToString()).And.NotContain(foreign.Id.ToString());
        other.Message.Should().NotContain(foreign.Id.ToString());
    }

    [Fact]
    public async Task VerifyRecord_OfAMissingRecord_AndOfAForeignRecord_BothAnswerNull()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await using var host = LedgerHost.Build(cs);
        var foreign = await host.InScopeAsync(sp => sp.GetRequiredService<EfAuditTrailWriter>().RecordAsync(Entry()));
        host.Context.TenantId = TestRequestContext.TenantB;

        (await host.InScopeAsync(sp => sp.GetRequiredService<IAuditQueryService>().VerifyRecordAsync(Guid.NewGuid()))).Should().BeNull();
        (await host.InScopeAsync(sp => sp.GetRequiredService<IAuditQueryService>().VerifyRecordAsync(foreign.Id))).Should().BeNull();
    }

    [Fact]
    public async Task IdempotencyConflict_KeepsInvalidOperationException_SkipsTheFailureLog_AndLeaksNoKeyOrResourceId()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        var logs = new InMemoryLoggerFactory();
        await using var host = LedgerHost.Build(cs, o => o.ConfigureServices = services => services.AddSingleton<ILoggerFactory>(logs));
        const string key = "idem-secret-key-7731";

        var first = await host.InScopeAsync(sp => sp.GetRequiredService<EfAuditTrailWriter>().RecordAsync(Entry("order-distinct-1", key)));
        var again = await host.InScopeAsync(sp => sp.GetRequiredService<EfAuditTrailWriter>().RecordAsync(Entry("order-distinct-1", key)));
        again.Id.Should().Be(first.Id);

        var conflict = () => host.InScopeAsync(sp => sp.GetRequiredService<EfAuditTrailWriter>().RecordAsync(Entry("order-distinct-2", key)));
        var thrown = (await conflict.Should().ThrowAsync<InvalidOperationException>()).Which;
        thrown.Message.Should().NotContain(key).And.NotContain("order-distinct-1").And.NotContain("order-distinct-2").And.Contain("'Order'");

        var records = logs.Loggers.Values.SelectMany(l => l.Records).ToList();
        records.ShouldHaveLogged(new EventId(6700));
        records.ShouldNotHaveLogged(new EventId(6701)); // a caller defect is not a failure-audit write failure
        records.ShouldNotLeak(key, "order-distinct-1", "order-distinct-2");
    }

    [GeneratedRegex(@"\{@?(?<name>[A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex Placeholder();

    [Fact]
    public void NoLoggerMessageTemplate_UsesABannedPlaceholder()
    {
        string[] banned = ["ActorId", "ActorTenantId", "ChainTenant", "TenantId", "IdempotencyKey", "ExceptionType", "MasterKeyId", "CorrelationId"];
        var violations = new List<string>();

        foreach (var type in typeof(EfAuditTrailWriter).Assembly.GetTypes())
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
