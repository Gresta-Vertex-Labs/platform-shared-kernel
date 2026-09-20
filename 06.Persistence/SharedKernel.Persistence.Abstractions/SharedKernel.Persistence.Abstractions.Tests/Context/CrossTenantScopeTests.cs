using System.Diagnostics.Metrics;
using FluentAssertions;
using SharedKernel.Persistence.Abstractions.Context;

namespace SharedKernel.Persistence.Abstractions.Tests.Context;

/// <summary>
/// <see cref="CrossTenantScope"/>: activation/deactivation semantics, nested-call composition, and
/// the <c>"SharedKernel.Persistence"</c> meter counter every <c>Enter(string?)</c> call records.
/// </summary>
public sealed class CrossTenantScopeTests
{
    [Fact]
    public void IsActive_Initially_False()
    {
        var scope = new CrossTenantScope();

        scope.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Enter_ActivatesScope_UntilDisposed()
    {
        var scope = new CrossTenantScope();

        using (scope.Enter("admin-1"))
        {
            scope.IsActive.Should().BeTrue();
        }

        scope.IsActive.Should().BeFalse("disposing the handle must deactivate the bypass");
    }

    [Fact]
    public void Enter_Nested_OnlyDeactivatesOnceEveryEntryDisposed()
    {
        var scope = new CrossTenantScope();

        var outer = scope.Enter("outer-actor");
        var inner = scope.Enter("inner-actor");

        scope.IsActive.Should().BeTrue();

        inner.Dispose();
        scope.IsActive.Should().BeTrue("the outer entry is still active");

        outer.Dispose();
        scope.IsActive.Should().BeFalse("every entry has now been disposed");
    }

    [Fact]
    public void Enter_DisposedTwice_IsIdempotent()
    {
        var scope = new CrossTenantScope();
        var handle = scope.Enter();

        handle.Dispose();
        var act = handle.Dispose;

        act.Should().NotThrow();
        scope.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Enter_WithActorId_RecordsMeterCounterTaggedWithThatActor()
    {
        var scope = new CrossTenantScope();
        var recorded = new List<(long Value, string? ActorId)>();

        using var listener = CreateListener(recorded);

        using (scope.Enter("data-migration-job"))
        {
        }

        recorded.Should().ContainSingle(m => m.Value == 1 && m.ActorId == "data-migration-job");
    }

    [Fact]
    public void Enter_WithNoActorId_RecordsUnknownTag()
    {
        var scope = new CrossTenantScope();
        var recorded = new List<(long Value, string? ActorId)>();

        using var listener = CreateListener(recorded);

        using (scope.Enter())
        {
        }

        recorded.Should().ContainSingle(m => m.Value == 1 && m.ActorId == "unknown");
    }

    [Fact]
    public void Enter_CalledTwice_RecordsTwoSeparateEntries()
    {
        // Every call site is an independent, attributable decision to bypass isolation — a
        // re-entrant call must not be silently folded into the first, or an admin path that enters
        // the scope from two different call sites in the same logical operation would undercount.
        var scope = new CrossTenantScope();
        var recorded = new List<(long Value, string? ActorId)>();

        using var listener = CreateListener(recorded);

        using (scope.Enter("actor-a"))
        using (scope.Enter("actor-b"))
        {
        }

        recorded.Should().HaveCount(2);
        recorded.Should().Contain(m => m.ActorId == "actor-a");
        recorded.Should().Contain(m => m.ActorId == "actor-b");
    }

    // Subscribes to every Counter<long> published under the "SharedKernel.Persistence" meter name and
    // appends each recorded measurement (plus its actor-id tag) to `into`.
    private static MeterListener CreateListener(List<(long Value, string? ActorId)> into)
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
            string? actorId = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "persistence.actor_id")
                    actorId = tag.Value as string;
            }

            into.Add((measurement, actorId));
        });

        listener.Start();
        return listener;
    }
}
