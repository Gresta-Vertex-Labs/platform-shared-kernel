using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Execution.Context;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.Abstractions.Diagnostics;
using SharedKernel.Testing.Telemetry;
using SharedKernel.Testing.Logging;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.Abstractions.Tests.Context;

/// <summary>
/// <see cref="CrossTenantScope"/>: per-scope activation that survives awaited helpers and never leaks to another
/// scope (A23, P-558/W3a), mandatory reason, actor captured from the request
/// context, logging and metering of every entry.
/// </summary>
public sealed class CrossTenantScopeTests
{
    private static async Task<IDisposable> EnterAfterYieldAsync(ICrossTenantScope scope, string reason)
    {
        await Task.Yield();
        return scope.Enter(reason);
    }

    private static CrossTenantScope NewScope(string actor = "admin-1", InMemoryLogger<CrossTenantScope>? logger = null) =>
        new(new FakeAuditActorContext(actor), logger ?? new InMemoryLogger<CrossTenantScope>());

    [Fact]
    public void IsActive_Initially_False()
    {
        NewScope().IsActive.Should().BeFalse();
    }

    [Fact]
    public void Enter_ActivatesScope_UntilDisposed()
    {
        var scope = NewScope();

        using (scope.Enter("report"))
        {
            scope.IsActive.Should().BeTrue();
        }

        scope.IsActive.Should().BeFalse("disposing the handle must deactivate the bypass");
    }

    [Fact]
    public async Task Enter_InsideAnAwaitedHelper_StaysActiveForTheCaller_UntilDisposed()
    {
        // The former flow-local (AsyncLocal) state was lost as soon as the async helper that entered it returned.
        var scope = NewScope();

        var handle = await EnterAfterYieldAsync(scope, "maintenance");

        scope.IsActive.Should().BeTrue("an entry made in an awaited helper must be visible to its caller");
        await Task.Yield();
        scope.IsActive.Should().BeTrue();

        handle.Dispose();
        scope.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Enter_InOneDependencyInjectionScope_IsSharedByThatScope_AndNotLeakedToAConcurrentScope()
    {
        using var provider = new ServiceCollection().AddLogging()
            .AddSingleton<IRequestContext>(new FakeAuditActorContext("svc"))
            .AddSharedKernelCrossTenantScope()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        using var entered = new SemaphoreSlim(0);
        using var observed = new SemaphoreSlim(0);

        var bypassing = Task.Run(async () =>
        {
            await using var scope = provider.CreateAsyncScope();
            var handle = await EnterAfterYieldAsync(scope.ServiceProvider.GetRequiredService<ICrossTenantScope>(), "report");

            // Every component of the same scope sees the entry, wherever it was made.
            scope.ServiceProvider.GetRequiredService<ICrossTenantScope>().IsActive.Should().BeTrue();

            entered.Release();
            (await observed.WaitAsync(TimeSpan.FromSeconds(10))).Should().BeTrue();
            handle.Dispose();
        });

        var unrelated = Task.Run(async () =>
        {
            await using var scope = provider.CreateAsyncScope();
            (await entered.WaitAsync(TimeSpan.FromSeconds(10))).Should().BeTrue();

            // Another request, running at the same moment, never sees the bypass.
            scope.ServiceProvider.GetRequiredService<ICrossTenantScope>().IsActive.Should().BeFalse();
            observed.Release();
        });

        await Task.WhenAll(bypassing, unrelated);
    }

    [Fact]
    public void Enter_OnAHandConstructedScope_DoesNotActivateAnotherInstance()
    {
        // The state belongs to the instance: the container's per-scope instance is the one components share.
        var entering = NewScope();
        var other = NewScope("someone-else");

        using (entering.Enter("migration"))
        {
            other.IsActive.Should().BeFalse();
        }
    }

    [Fact]
    public void Enter_Nested_OnlyDeactivatesOnceEveryEntryDisposed()
    {
        var scope = NewScope();

        var outer = scope.Enter("outer");
        var inner = scope.Enter("inner");

        inner.Dispose();
        scope.IsActive.Should().BeTrue("the outer entry is still active");

        outer.Dispose();
        scope.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Enter_DisposedTwice_IsIdempotent()
    {
        var scope = NewScope();
        var outer = scope.Enter("outer");
        var handle = scope.Enter("inner");

        handle.Dispose();
        handle.Dispose();

        scope.IsActive.Should().BeTrue("a second dispose must not release the outer entry");
        outer.Dispose();
        scope.IsActive.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Enter_WithoutReason_Throws(string reason)
    {
        var scope = NewScope();
        var act = () => scope.Enter(reason);

        act.Should().Throw<ArgumentException>();
        scope.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Enter_LogsTheActorKindOnly_NeverTheActorItsTenantOrTheReason()
    {
        var logger = new InMemoryLogger<CrossTenantScope>();
        var tenant = new SharedKernel.Execution.Tenancy.TenantId(Guid.Parse("abcdef01-2345-6789-abcd-ef0123456789"));

        using (new CrossTenantScope(new FakeAuditActorContext("data-migration-job-7f3a", tenant), logger).Enter("backfill invoices"))
        {
        }

        var record = logger.Records.Should().ContainSingle().Subject;
        record.EventId.Id.Should().Be(6150);
        record.Message.Should().Be("A cross-tenant scope was entered by a User actor.");
        logger.Records.ShouldNotLeak("data-migration-job-7f3a", tenant.ToString(), "abcdef01-2345-6789-abcd-ef0123456789", "backfill invoices");
    }

    [Fact]
    public void Enter_CalledTwice_RecordsTwoEntriesTaggedWithActorKind()
    {
        using var metrics = MetricRecorder.StartRecording(PersistenceTelemetry.Name);

        var scope = NewScope("meter-actor-unique");
        using (scope.Enter("a"))
        using (scope.Enter("b"))
        {
        }

        metrics
            .ShouldHaveInstrument("sharedkernel.persistence.cross_tenant_scope_entries", unit: "{entry}", kind: InstrumentKind.Counter)
            .ShouldHaveTagSet("sharedkernel.persistence.cross_tenant_scope_entries", "persistence.actor_kind")
            .ShouldHaveSum("sharedkernel.persistence.cross_tenant_scope_entries", 2, new KeyValuePair<string, object?>("persistence.actor_kind", nameof(ActorKind.User)))
            .ShouldFollowMetricConvention();
    }

    [Fact]
    public void AddSharedKernelCrossTenantScope_RegistersScopedScopeAndAnonymousDefault()
    {
        var services = new ServiceCollection().AddLogging().AddSharedKernelCrossTenantScope();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ICrossTenantScope>().Should().BeOfType<CrossTenantScope>();
        scope.ServiceProvider.GetRequiredService<IRequestContext>().Should().BeSameAs(AnonymousRequestContext.Instance);
    }

    [Fact]
    public void AddSharedKernelCrossTenantScope_KeepsAnExistingRequestContext()
    {
        var existing = new FakeAuditActorContext("svc");
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<IRequestContext>(existing);
        services.AddSharedKernelCrossTenantScope();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IRequestContext>().Should().BeSameAs(existing);
    }
}
