using System.Diagnostics;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Testing.Telemetry;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Telemetry;

public sealed class ActivityAssertionsTests : IDisposable
{
    private readonly SpanFactory _spans = new();

    public void Dispose() => _spans.Dispose();

    private Activity Make(Action<Activity>? configure = null, string name = "messaging publish", ActivityKind kind = ActivityKind.Producer) =>
        _spans.Make(name, kind, configure);

    private static void AddExceptionEvent(Activity activity, Exception exception, bool complete = true)
    {
        var tags = new ActivityTagsCollection
        {
            ["exception.type"] = exception.GetType().FullName,
            ["exception.message"] = exception.Message,
        };
        if (complete)
        {
            tags["exception.stacktrace"] = exception.ToString();
        }

        activity.AddEvent(new ActivityEvent("exception", tags: tags));
    }

    private static InvalidOperationException Fails(Func<object> assertion) => Assert.Throws<InvalidOperationException>(assertion);

    // ---- selection -------------------------------------------------------------------------------------

    [Fact]
    public void Selection_PassesAndFails()
    {
        var one = Make(name: "messaging publish");
        var two = Make(name: "messaging publish");
        var other = Make(name: "storage upload", kind: ActivityKind.Client);
        Activity[] all = [one, two, other];

        Assert.Same(one, all.ShouldHaveSpan("messaging publish"));
        Assert.Same(other, all.ShouldHaveSingleSpan("storage upload"));
        Assert.Equal(3, all.ShouldNotHaveSpan("missing").Count());
        Assert.Equal(3, all.ShouldHaveSpanCount("messaging publish", 2).Count());

        var missing = Fails(() => all.ShouldHaveSpan("nope"));
        Assert.Contains("'nope'", missing.Message);
        Assert.Contains("'messaging publish'", missing.Message);
        Assert.Contains("found 2", Fails(() => all.ShouldHaveSingleSpan("messaging publish")).Message);
        Assert.Contains("found 0", Fails(() => all.ShouldHaveSingleSpan("nope")).Message);
        Assert.Contains("no span named", Fails(() => all.ShouldNotHaveSpan("storage upload")).Message);
        Assert.Contains("Expected 1 span(s)", Fails(() => all.ShouldHaveSpanCount("messaging publish", 1)).Message);
    }

    // ---- shape -----------------------------------------------------------------------------------------

    [Fact]
    public void KindSourceTagsEventsAndParent_PassAndFail()
    {
        Activity? child = null;
        var parent = Make(a =>
        {
            a.SetTag("destination", "orders");
            a.SetTag("attempt", 2);
            a.AddEvent(new ActivityEvent("retry"));
            child = _spans.Source.StartActivity("child op", ActivityKind.Internal);
            child!.Stop();
        });

        Assert.Same(parent, parent.ShouldHaveKind(ActivityKind.Producer));
        parent.ShouldHaveSource(_spans.Source.Name)
            .ShouldHaveTag("destination", "orders")
            .ShouldHaveTag("attempt", 2)
            .ShouldHaveTagKeys("attempt", "destination")
            .ShouldNotHaveTag("missing")
            .ShouldHaveEvent("retry");
        child!.ShouldHaveParent(parent);

        Assert.Contains("kind 'Consumer', but it was 'Producer'", Fails(() => parent.ShouldHaveKind(ActivityKind.Consumer)).Message);
        Assert.Contains("source 'x'", Fails(() => parent.ShouldHaveSource("x")).Message);
        Assert.Contains("'orders' (String)", Fails(() => parent.ShouldHaveTag("destination", "invoices")).Message);
        Assert.Contains("'2' (Int32)", Fails(() => parent.ShouldHaveTag("attempt", 2L)).Message);
        Assert.Contains("missing; tags: [destination, attempt]", Fails(() => parent.ShouldHaveTag("nothing", "x")).Message);
        var keys = Fails(() => parent.ShouldHaveTagKeys("destination", "extra"));
        Assert.Contains("missing [extra]", keys.Message);
        Assert.Contains("unexpected [attempt]", keys.Message);
        Assert.Contains("no tag 'destination'", Fails(() => parent.ShouldNotHaveTag("destination")).Message);
        Assert.Contains("an event named 'other'", Fails(() => parent.ShouldHaveEvent("other")).Message);
        Assert.Contains("parent span", Fails(() => parent.ShouldHaveParent(child!)).Message);
    }

    [Fact]
    public void Status_PassesAndFails()
    {
        var unset = Make();
        var failed = Make(a => a.SetStatus(ActivityStatusCode.Error, "orders.unavailable"));

        unset.ShouldBeUnset().ShouldHaveStatus(ActivityStatusCode.Unset);
        failed.ShouldHaveStatus(ActivityStatusCode.Error).ShouldHaveStatus(ActivityStatusCode.Error, "orders.unavailable");

        Assert.Contains("status 'Error', but it was 'Unset'", Fails(() => unset.ShouldHaveStatus(ActivityStatusCode.Error)).Message);
        Assert.Contains("status 'Unset'", Fails(() => failed.ShouldBeUnset()).Message);
        Assert.Contains("status description 'wrong'", Fails(() => failed.ShouldHaveStatus(ActivityStatusCode.Error, "wrong")).Message);
    }

    // ---- the P-580 shapes ------------------------------------------------------------------------------

    [Theory]
    [InlineData(ErrorType.NotFound, "NotFound")]
    [InlineData(ErrorType.Timeout, "Timeout")]
    [InlineData((ErrorType)999, "_OTHER")]
    public void RecordedFailure_PassesOnTheCorrectShape(ErrorType type, string expectedTypeName)
    {
        var error = new Error("orders.failed", "Failed.", type);
        var span = Make(a =>
        {
            a.SetStatus(ActivityStatusCode.Error, error.Code);
            a.SetTag(WellKnownTagKeys.ErrorCode, error.Code);
            a.SetTag(WellKnownTagKeys.ErrorType, expectedTypeName);
        });

        span.ShouldBeRecordedFailure(error);
    }

    [Fact]
    public void RecordedFailure_FailsOnEveryDeviation_AndListsThemAll()
    {
        var error = Error.NotFound("orders.missing", "Missing.");

        var untouched = Fails(() => Make().ShouldBeRecordedFailure(error));
        Assert.Contains("status should be 'Error'", untouched.Message);
        Assert.Contains("status description should be 'orders.missing'", untouched.Message);
        Assert.Contains("tag 'error.code' should be 'orders.missing' (String), but it is missing", untouched.Message);
        Assert.Contains("tag 'error.type' should be 'NotFound' (String), but it is missing", untouched.Message);

        var messageAsDescription = Make(a =>
        {
            a.SetStatus(ActivityStatusCode.Error, "Missing.");
            a.SetTag(WellKnownTagKeys.ErrorCode, error.Code);
            a.SetTag(WellKnownTagKeys.ErrorType, "NotFound");
        });
        Assert.Contains("description should be 'orders.missing'", Fails(() => messageAsDescription.ShouldBeRecordedFailure(error)).Message);

        var ok = Make(a =>
        {
            a.SetStatus(ActivityStatusCode.Ok);
            a.SetTag(WellKnownTagKeys.ErrorCode, error.Code);
            a.SetTag(WellKnownTagKeys.ErrorType, "NotFound");
        });
        Assert.Contains("status should be 'Error', but it was 'Ok'", Fails(() => ok.ShouldBeRecordedFailure(error)).Message);

        var withEvent = Make(a =>
        {
            a.SetStatus(ActivityStatusCode.Error, error.Code);
            a.SetTag(WellKnownTagKeys.ErrorCode, error.Code);
            a.SetTag(WellKnownTagKeys.ErrorType, "NotFound");
            AddExceptionEvent(a, new InvalidOperationException("x"));
        });
        Assert.Contains("must not add an 'exception' event", Fails(() => withEvent.ShouldBeRecordedFailure(error)).Message);

        var wrongType = Make(a =>
        {
            a.SetStatus(ActivityStatusCode.Error, error.Code);
            a.SetTag(WellKnownTagKeys.ErrorCode, error.Code);
            a.SetTag(WellKnownTagKeys.ErrorType, "Conflict");
        });
        Assert.Contains("tag 'error.type' should be 'NotFound' (String), but it was 'Conflict' (String)", Fails(() => wrongType.ShouldBeRecordedFailure(error)).Message);
    }

    [Fact]
    public void RecordedException_PassesWithAndWithoutCode()
    {
        var exception = new InvalidOperationException("secret detail");
        var typeName = typeof(InvalidOperationException).FullName!;

        var plain = Make(a =>
        {
            a.SetStatus(ActivityStatusCode.Error, typeName);
            a.SetTag(WellKnownTagKeys.ErrorType, typeName);
            AddExceptionEvent(a, exception);
        });
        plain.ShouldBeRecordedException(typeof(InvalidOperationException));

        var coded = Make(a =>
        {
            a.SetStatus(ActivityStatusCode.Error, typeName);
            a.SetTag(WellKnownTagKeys.ErrorType, typeName);
            a.SetTag(WellKnownTagKeys.ErrorCode, "orders.gone");
            AddExceptionEvent(a, exception);
        });
        coded.ShouldBeRecordedException(typeof(InvalidOperationException), "orders.gone");
    }

    [Fact]
    public void RecordedException_FailsOnEveryDeviation()
    {
        var exception = new InvalidOperationException("secret detail");
        var typeName = typeof(InvalidOperationException).FullName!;

        var messageAsDescription = Make(a =>
        {
            a.SetStatus(ActivityStatusCode.Error, exception.Message);
            a.SetTag(WellKnownTagKeys.ErrorType, typeName);
            AddExceptionEvent(a, exception);
        });
        Assert.Contains($"status description should be '{typeName}'", Fails(() => messageAsDescription.ShouldBeRecordedException(typeof(InvalidOperationException))).Message);

        var noEvent = Make(a =>
        {
            a.SetStatus(ActivityStatusCode.Error, typeName);
            a.SetTag(WellKnownTagKeys.ErrorType, typeName);
        });
        Assert.Contains("no 'exception' event was added", Fails(() => noEvent.ShouldBeRecordedException(typeof(InvalidOperationException))).Message);

        var incompleteEvent = Make(a =>
        {
            a.SetStatus(ActivityStatusCode.Error, typeName);
            a.SetTag(WellKnownTagKeys.ErrorType, typeName);
            AddExceptionEvent(a, exception, complete: false);
        });
        Assert.Contains("exception.stacktrace", Fails(() => incompleteEvent.ShouldBeRecordedException(typeof(InvalidOperationException))).Message);

        var unexpectedCode = Make(a =>
        {
            a.SetStatus(ActivityStatusCode.Error, typeName);
            a.SetTag(WellKnownTagKeys.ErrorType, typeName);
            a.SetTag(WellKnownTagKeys.ErrorCode, "orders.gone");
            AddExceptionEvent(a, exception);
        });
        Assert.Contains("should be absent", Fails(() => unexpectedCode.ShouldBeRecordedException(typeof(InvalidOperationException))).Message);
        Assert.Contains("tag 'error.code' should be 'other.code' (String)", Fails(() => unexpectedCode.ShouldBeRecordedException(typeof(InvalidOperationException), "other.code")).Message);
        Assert.Contains("tag 'error.type' should be", Fails(() => unexpectedCode.ShouldBeRecordedException(typeof(ArgumentException), "orders.gone")).Message);
    }

    [Fact]
    public void RecordedCancellationAndSuccess_PassOnQuietSpans_AndFailOnAnyErrorTrace()
    {
        var quiet = Make();
        quiet.ShouldBeRecordedCancellation().ShouldBeRecordedSuccess();

        var ok = Make(a => a.SetStatus(ActivityStatusCode.Ok));
        Assert.Contains("status should be 'Unset' for success, but it was 'Ok'", Fails(() => ok.ShouldBeRecordedSuccess()).Message);

        var errored = Make(a =>
        {
            a.SetStatus(ActivityStatusCode.Error, "x");
            a.SetTag(WellKnownTagKeys.ErrorType, "System.OperationCanceledException");
            AddExceptionEvent(a, new OperationCanceledException());
        });
        var message = Fails(() => errored.ShouldBeRecordedCancellation()).Message;
        Assert.Contains("status should be 'Unset' for a cancellation", message);
        Assert.Contains("tag 'error.type' should be absent", message);
        Assert.Contains("must not add an 'exception' event", message);

        var withCode = Make(a => a.SetTag(WellKnownTagKeys.ErrorCode, "x"));
        Assert.Contains("tag 'error.code' should be absent", Fails(() => withCode.ShouldBeRecordedSuccess()).Message);
    }

    [Fact]
    public void Outcome_IsConstrainedToTheVocabulary()
    {
        var span = Make(a => a.SetTag(WellKnownTagKeys.Outcome, WellKnownOutcomes.Failure));

        span.ShouldHaveOutcome(WellKnownOutcomes.Failure);
        Assert.Contains("'success'", Fails(() => span.ShouldHaveOutcome(WellKnownOutcomes.Success)).Message);
        Assert.Contains("is not an outcome", Fails(() => span.ShouldHaveOutcome("failed")).Message);
        Assert.Contains("missing", Fails(() => Make().ShouldHaveOutcome(WellKnownOutcomes.Success)).Message);
    }

    // ---- hygiene and conventions -----------------------------------------------------------------------

    [Fact]
    public void ShouldNotLeak_ScansTagsBaggageStatusAndEvents_ButExemptsTheExceptionEvent()
    {
        var clean = Make(a =>
        {
            a.SetTag("tenant", "hashed");
            AddExceptionEvent(a, new InvalidOperationException("tenant-42 not found"));
        });
        clean.ShouldNotLeak("tenant-42");
        new[] { clean }.ShouldNotLeak("tenant-42");

        Assert.Contains("tag 'who'", Fails(() => Make(a => a.SetTag("who", "tenant-42")).ShouldNotLeak("tenant-42")).Message);
        Assert.Contains("baggage 'b'", Fails(() => Make(a => a.AddBaggage("b", "tenant-42")).ShouldNotLeak("tenant-42")).Message);
        Assert.Contains("status description", Fails(() => Make(a => a.SetStatus(ActivityStatusCode.Error, "tenant-42")).ShouldNotLeak("tenant-42")).Message);
        Assert.Contains("event 'note' tag 'k'", Fails(() => Make(a => a.AddEvent(new ActivityEvent("note", tags: new ActivityTagsCollection { ["k"] = "tenant-42" }))).ShouldNotLeak("tenant-42")).Message);
        Assert.Contains("span 'messaging publish'", Fails(() => new[] { Make(a => a.SetTag("who", "tenant-42")) }.ShouldNotLeak("tenant-42")).Message);
        Assert.Throws<ArgumentException>(() => clean.ShouldNotLeak(""));
    }

    [Fact]
    public void ShouldFollowSpanConvention_AcceptsTheConventionalSpan_AndListsEveryViolation()
    {
        using var conventional = new SpanFactory("SharedKernel.SelfTest");
        var good = conventional.Make("selftest publish", ActivityKind.Producer, a => a.SetTag("messaging.destination", "orders"));
        good.ShouldFollowSpanConvention();
        new[] { good }.ShouldFollowSpanConvention();

        var bad = Make(a => a.SetTag("Bad-Key", 1), name: "Messaging Publish", kind: ActivityKind.Server);
        var message = Fails(() => bad.ShouldFollowSpanConvention()).Message;
        Assert.Contains("source", message);
        Assert.Contains("name 'Messaging Publish'", message);
        Assert.Contains("kind 'Server'", message);
        Assert.Contains("tag key 'Bad-Key'", message);
        Assert.Contains("violates 4 convention(s)", message);
        Assert.Contains("span 'Messaging Publish'", Fails(() => new[] { bad }.ShouldFollowSpanConvention()).Message);
    }
}
