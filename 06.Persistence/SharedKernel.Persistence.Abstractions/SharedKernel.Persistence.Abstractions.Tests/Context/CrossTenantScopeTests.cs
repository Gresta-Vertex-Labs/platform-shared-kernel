using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.Context;
using SharedKernel.Persistence.Abstractions.Context;
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
        new(new FakeAuditActorContext(actor), logger);

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
        using var provider = new ServiceCollection()
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
    public void Enter_LogsActorAndReason()
    {
        var logger = new InMemoryLogger<CrossTenantScope>();

        using (NewScope("data-migration-job", logger).Enter("backfill invoices"))
        {
        }

        var record = logger.Records.Should().ContainSingle().Subject;
        record.EventId.Id.Should().Be(6150);
        record.Message.Should().Contain("data-migration-job").And.Contain("backfill invoices");
    }

    [Fact]
    public void Enter_CalledTwice_RecordsTwoEntriesTaggedWithActorKind()
    {
        var recorded = new List<(long Value, string? ActorKind)>();
        using var listener = CreateListener(recorded);

        var scope = NewScope("meter-actor-unique");
        using (scope.Enter("a"))
        using (scope.Enter("b"))
        {
        }

        lock (recorded)
            recorded.Count(m => m.ActorKind == nameof(ActorKind.User)).Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void AddSharedKernelCrossTenantScope_RegistersScopedScopeAndAnonymousDefault()
    {
        var services = new ServiceCollection().AddSharedKernelCrossTenantScope();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ICrossTenantScope>().Should().BeOfType<CrossTenantScope>();
        scope.ServiceProvider.GetRequiredService<IRequestContext>().Should().BeSameAs(AnonymousRequestContext.Instance);
    }

    [Fact]
    public void AddSharedKernelCrossTenantScope_KeepsAnExistingRequestContext()
    {
        var existing = new FakeAuditActorContext("svc");
        var services = new ServiceCollection();
        services.AddSingleton<IRequestContext>(existing);
        services.AddSharedKernelCrossTenantScope();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IRequestContext>().Should().BeSameAs(existing);
    }

    private static MeterListener CreateListener(List<(long Value, string? ActorKind)> into)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == "SharedKernel.Persistence"
                    && instrument.Name == "persistence.cross_tenant_scope_entries")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };

        listener.SetMeasurementEventCallback<long>((_, measurement, tags, _) =>
        {
            string? actorKind = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "persistence.actor_kind")
                    actorKind = tag.Value as string;
            }

            lock (into)
                into.Add((measurement, actorKind));
        });

        listener.Start();
        return listener;
    }
}
