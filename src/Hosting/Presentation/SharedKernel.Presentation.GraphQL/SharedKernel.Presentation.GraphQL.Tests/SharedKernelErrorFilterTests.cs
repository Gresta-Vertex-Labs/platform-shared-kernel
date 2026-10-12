using HotChocolate;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Presentation.GraphQL.Errors;
using SharedKernel.Primitives.Errors;
using ErrorCodes = SharedKernel.Primitives.Errors.ErrorCodes;
using SharedKernel.Testing.Communication;

namespace SharedKernel.Presentation.GraphQL.Tests;

/// <summary>
/// The error filter outside a request (an in-process executor): production rules, nothing translated, no correlation
/// id; the cases that need a real request are in <c>GraphQLErrorShapeTests</c>.
/// </summary>
public sealed class SharedKernelErrorFilterTests
{
    private readonly SharedKernelErrorFilter _filter = new(new FakeHttpContextAccessor(), NullLogger<SharedKernelErrorFilter>.Instance);

    private static IError BuildError(
        string message = "Test error",
        Exception? exception = null,
        IReadOnlyDictionary<string, object?>? extensions = null)
    {
        var builder = ErrorBuilder.New().SetMessage(message);
        if (exception is not null) builder.SetException(exception);
        if (extensions is not null)
        {
            foreach (var kv in extensions)
                builder.SetExtension(kv.Key, kv.Value);
        }
        return builder.Build();
    }

    [Fact]
    public void ErrorWithoutAnException_IsARequestError_400()
    {
        var result = _filter.OnError(BuildError("Syntax error"));

        result.Extensions!["status"].Should().Be(400);
        result.Extensions["errorCode"].Should().Be(GraphQLErrorCodes.RequestInvalid);
        result.Extensions["title"].Should().Be("Bad Request");
        result.Extensions["detail"].Should().Be("Syntax error");
        result.Message.Should().Be("Syntax error");
    }

    [Fact]
    public void TheStatusExtensionOfTheInput_IsNotTrusted()
    {
        var result = _filter.OnError(BuildError(extensions: new Dictionary<string, object?> { ["status"] = 200 }));

        result.Extensions!["status"].Should().Be(400);
    }

    [Fact]
    public void Exception_IsClassifiedLikeHttp_AndItsMessageNeverShown()
    {
        var result = _filter.OnError(BuildError("Unexpected Execution Error", new InvalidOperationException("inner detail")));

        result.Extensions!["status"].Should().Be(500);
        result.Extensions["errorCode"].Should().Be(ErrorCodes.Unexpected.Default);
        result.Extensions["detail"].Should().Be("An unexpected error occurred.");
        result.Extensions["title"].Should().Be("An error occurred while processing your request.", "the framework's own title for a 500");
        result.Extensions.Should().NotContainKey("exception", "outside a request the environment is production");
        result.Message.Should().Be("An unexpected error occurred.");
    }

    [Fact]
    public void ExistingExtensions_ArePreserved()
    {
        var result = _filter.OnError(BuildError(extensions: new Dictionary<string, object?> { ["custom"] = "value", ["code"] = "HC0001" }));

        result.Extensions!["custom"].Should().Be("value");
        result.Extensions["code"].Should().Be("HC0001");
    }

    [Fact]
    public void WithoutARequest_ThereIsNoCorrelationId_AndNoTraceIdWithoutAnActivity()
    {
        System.Diagnostics.Activity.Current = null;

        var result = _filter.OnError(BuildError());

        result.Extensions.Should().NotContainKey("correlationId");
        result.Extensions!["traceId"].Should().BeNull("there is neither an activity nor a request");
    }

    [Fact]
    public void TheTraceId_IsTheRequestsIdentifier_WithoutAnActivity()
    {
        System.Diagnostics.Activity.Current = null;
        var accessor = new FakeHttpContextAccessor(new DefaultHttpContext { TraceIdentifier = "kestrel-id" });
        var filter = new SharedKernelErrorFilter(accessor, NullLogger<SharedKernelErrorFilter>.Instance);

        var result = filter.OnError(BuildError());

        result.Extensions!["traceId"].Should().Be("kestrel-id");
    }

    [Fact]
    public void TheTraceId_IsTheCurrentActivitysId_WhenThereIsOne()
    {
        using var source = new System.Diagnostics.ActivitySource("Test.GraphQLTraceId." + Guid.NewGuid().ToString("N"));
        using var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = candidate => candidate == source,
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) =>
                System.Diagnostics.ActivitySamplingResult.AllDataAndRecorded,
        };
        System.Diagnostics.ActivitySource.AddActivityListener(listener);
        using var activity = source.StartActivity("request");
        var accessor = new FakeHttpContextAccessor(new DefaultHttpContext { TraceIdentifier = "kestrel-id" });
        var filter = new SharedKernelErrorFilter(accessor, NullLogger<SharedKernelErrorFilter>.Instance);

        var result = filter.OnError(BuildError());

        activity.Should().NotBeNull();
        result.Extensions!["traceId"].Should().Be(activity!.Id);
    }

    [Fact]
    public void AnExceptionThrownBecauseTheClientWentAway_IsPassedThrough()
    {
        using var aborted = new CancellationTokenSource();
        aborted.Cancel();
        var context = new DefaultHttpContext { RequestAborted = aborted.Token };
        var filter = new SharedKernelErrorFilter(new FakeHttpContextAccessor(context), NullLogger<SharedKernelErrorFilter>.Instance);
        var error = BuildError("The operation was canceled.", new OperationCanceledException());

        filter.OnError(error).Should().BeSameAs(error);
    }

    [Fact]
    public void Constructor_RequiresItsDependencies()
    {
        var accessor = () => new SharedKernelErrorFilter(null!, NullLogger<SharedKernelErrorFilter>.Instance);
        var logger = () => new SharedKernelErrorFilter(new FakeHttpContextAccessor(), null!);

        accessor.Should().Throw<ArgumentNullException>();
        logger.Should().Throw<ArgumentNullException>();
    }
}
