using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.Context;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Testing.Logging;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.Abstractions.Tests.Context;

/// <summary>
/// <see cref="CrossTenantScope"/>: flow-wide activation (A23), mandatory reason, actor captured from the request
/// context, logging and metering of every entry.
/// </summary>
public sealed class CrossTenantScopeTests
{
    private static CrossTenantScope NewScope(string actor = "admin-1", InMemoryLogger<CrossTenantScope>? logger = null) =>
        new(new FakeAuditActorContext(actor), logger);

    [Fact]
    public void IsActive_Initially_False()
    {
        NewScope().IsActive.Should().BeFalse();
        CrossTenantScope.IsActiveInCurrentFlow.Should().BeFalse();
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
    public void Enter_OnOneInstance_IsObservedByEveryOtherInstanceInTheFlow()
    {
        // A23: the former per-instance counter made a separately constructed scope a silent no-op.
        var entering = NewScope();
        var observer = NewScope("someone-else");

        using (entering.Enter("migration"))
        {
            observer.IsActive.Should().BeTrue();
            CrossTenantScope.IsActiveInCurrentFlow.Should().BeTrue();
        }

        observer.IsActive.Should().BeFalse();
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
        var act = () => NewScope().Enter(reason);

        act.Should().Throw<ArgumentException>();
        CrossTenantScope.IsActiveInCurrentFlow.Should().BeFalse();
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
