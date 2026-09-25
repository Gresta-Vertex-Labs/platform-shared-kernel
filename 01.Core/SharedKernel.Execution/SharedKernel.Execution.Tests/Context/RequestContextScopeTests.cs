using FluentAssertions;
using SharedKernel.Execution.Context;

namespace SharedKernel.Execution.Tests.Context;

public sealed class RequestContextScopeTests
{
    private static SystemRequestContext Context(string identity) => new([], identity);

    [Fact]
    public void Current_IsNullOutsideAnyScope()
    {
        RequestContextScope.Current.Should().BeNull();
        new RequestContextAccessor().Current.Should().BeNull();
    }

    [Fact]
    public void Begin_MakesTheContextAmbientUntilDisposed()
    {
        var context = Context("job-a");

        using (RequestContextScope.Begin(context))
        {
            RequestContextScope.Current.Should().BeSameAs(context);
            new RequestContextAccessor().Current.Should().BeSameAs(context);
        }

        RequestContextScope.Current.Should().BeNull();
    }

    [Fact]
    public void NestedScopes_RestoreThePreviousContext()
    {
        var outer = Context("outer");
        var inner = Context("inner");

        using (RequestContextScope.Begin(outer))
        {
            using (RequestContextScope.Begin(inner))
                RequestContextScope.Current.Should().BeSameAs(inner);

            RequestContextScope.Current.Should().BeSameAs(outer);
        }
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var outer = Context("outer");

        using (RequestContextScope.Begin(outer))
        {
            var inner = RequestContextScope.Begin(Context("inner"));
            inner.Dispose();

            using (RequestContextScope.Begin(Context("second")))
            {
                inner.Dispose(); // a second dispose must not restore "outer" over "second"
                RequestContextScope.Current!.UserId.Should().Be("second");
            }

            RequestContextScope.Current.Should().BeSameAs(outer);
        }
    }

    [Fact]
    public async Task Scope_FlowsIntoAwaitedContinuationsAndTasks()
    {
        var context = Context("flow");

        using (RequestContextScope.Begin(context))
        {
            await Task.Yield();
            RequestContextScope.Current.Should().BeSameAs(context);

            var seenByTask = await Task.Run(() => RequestContextScope.Current);
            seenByTask.Should().BeSameAs(context);
        }
    }

    [Fact]
    public async Task ScopeOpenedInsideAnAwaitedMethod_IsNotVisibleToTheCaller()
    {
        await OpenScopeWithoutDisposingAsync();

        RequestContextScope.Current.Should().BeNull();
    }

    [Fact]
    public async Task ConcurrentFlows_DoNotSeeEachOthersContext()
    {
        async Task<string?> RunAsync(string identity)
        {
            using (RequestContextScope.Begin(Context(identity)))
            {
                await Task.Delay(10);
                return RequestContextScope.Current?.UserId;
            }
        }

        var results = await Task.WhenAll(RunAsync("a"), RunAsync("b"), RunAsync("c"));

        results.Should().Equal("a", "b", "c");
    }

    [Fact]
    public void Begin_RejectsNull()
    {
        var act = () => RequestContextScope.Begin(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CorrelationId_DefaultsToNull()
    {
        IRequestContext context = Context("job");

        context.CorrelationId.Should().BeNull();
    }

    private static async Task OpenScopeWithoutDisposingAsync()
    {
        await Task.Yield();
        _ = RequestContextScope.Begin(Context("leaked"));
    }
}
