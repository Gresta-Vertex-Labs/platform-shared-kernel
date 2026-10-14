using System.Diagnostics;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Propagation;

namespace SharedKernel.Testing.Telemetry;

/// <summary>
/// Read-only assertions over recorded <see cref="Activity"/> spans (typically
/// <see cref="ActivityRecorder.RecordedActivities"/>).
/// </summary>
/// <remarks>
/// Every member throws <see cref="InvalidOperationException"/> with a readable message and returns its subject so
/// assertions chain. The failure shapes (<see cref="ShouldBeRecordedFailure"/>, <see cref="ShouldBeRecordedException"/>,
/// <see cref="ShouldBeRecordedCancellation"/>, <see cref="ShouldBeRecordedSuccess"/>) state what
/// <c>SharedKernel.Primitives.Telemetry.ActivityFailureExtensions</c> produces literally; they never call it.
/// </remarks>
public static class ActivityAssertions
{
    // ---- Selection --------------------------------------------------------------------------------------

    /// <summary>Asserts a span named <paramref name="name"/> exists.</summary>
    /// <param name="activities">The recorded spans.</param>
    /// <param name="name">The span (operation) name.</param>
    /// <returns>The first span with that name.</returns>
    /// <exception cref="InvalidOperationException">No such span.</exception>
    public static Activity ShouldHaveSpan(this IEnumerable<Activity> activities, string name)
    {
        ArgumentNullException.ThrowIfNull(activities);
        var list = activities.ToList();
        return list.FirstOrDefault(a => a.OperationName == name)
            ?? throw new InvalidOperationException($"Expected a span named '{name}', but found {Describe(list)}.");
    }

    /// <summary>Asserts exactly one span named <paramref name="name"/> exists.</summary>
    /// <param name="activities">The recorded spans.</param>
    /// <param name="name">The span (operation) name.</param>
    /// <returns>That span.</returns>
    /// <exception cref="InvalidOperationException">There is none, or more than one.</exception>
    public static Activity ShouldHaveSingleSpan(this IEnumerable<Activity> activities, string name)
    {
        ArgumentNullException.ThrowIfNull(activities);
        var list = activities.ToList();
        var matches = list.Where(a => a.OperationName == name).ToList();
        return matches.Count == 1
            ? matches[0]
            : throw new InvalidOperationException(
                $"Expected exactly one span named '{name}', but found {matches.Count} among {Describe(list)}.");
    }

    /// <summary>Asserts no span named <paramref name="name"/> exists.</summary>
    /// <param name="activities">The recorded spans.</param>
    /// <param name="name">The span (operation) name.</param>
    /// <returns>The same spans.</returns>
    /// <exception cref="InvalidOperationException">A span with that name exists.</exception>
    public static IEnumerable<Activity> ShouldNotHaveSpan(this IEnumerable<Activity> activities, string name)
    {
        ArgumentNullException.ThrowIfNull(activities);
        var list = activities.ToList();
        if (list.Any(a => a.OperationName == name))
        {
            throw new InvalidOperationException($"Expected no span named '{name}', but one was recorded.");
        }

        return list;
    }

    /// <summary>Asserts exactly <paramref name="count"/> spans are named <paramref name="name"/>.</summary>
    /// <param name="activities">The recorded spans.</param>
    /// <param name="name">The span (operation) name.</param>
    /// <param name="count">The expected number of spans with that name.</param>
    /// <returns>The same spans.</returns>
    /// <exception cref="InvalidOperationException">The count differs.</exception>
    public static IEnumerable<Activity> ShouldHaveSpanCount(this IEnumerable<Activity> activities, string name, int count)
    {
        ArgumentNullException.ThrowIfNull(activities);
        var list = activities.ToList();
        var actual = list.Count(a => a.OperationName == name);
        if (actual != count)
        {
            throw new InvalidOperationException(
                $"Expected {count} span(s) named '{name}', but found {actual} among {Describe(list)}.");
        }

        return list;
    }

    // ---- Shape ------------------------------------------------------------------------------------------

    /// <summary>Asserts the span kind.</summary>
    /// <param name="activity">The span.</param>
    /// <param name="kind">The expected kind.</param>
    /// <returns>The span.</returns>
    /// <exception cref="InvalidOperationException">The kind differs.</exception>
    public static Activity ShouldHaveKind(this Activity activity, ActivityKind kind)
    {
        ArgumentNullException.ThrowIfNull(activity);
        return activity.Kind == kind
            ? activity
            : throw Fail(activity, $"kind '{kind}', but it was '{activity.Kind}'");
    }

    /// <summary>Asserts the name of the <see cref="ActivitySource"/> that produced the span.</summary>
    /// <param name="activity">The span.</param>
    /// <param name="sourceName">The expected source name.</param>
    /// <returns>The span.</returns>
    /// <exception cref="InvalidOperationException">The source name differs.</exception>
    public static Activity ShouldHaveSource(this Activity activity, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(activity);
        return activity.Source.Name == sourceName
            ? activity
            : throw Fail(activity, $"source '{sourceName}', but it was '{activity.Source.Name}'");
    }

    /// <summary>
    /// Asserts the tag <paramref name="key"/> exists and equals <paramref name="value"/> (<see cref="object.Equals(object?, object?)"/>
    /// on the stored value, so an <c>int</c> tag does not equal a <c>long</c> expectation).
    /// </summary>
    /// <param name="activity">The span.</param>
    /// <param name="key">The tag key.</param>
    /// <param name="value">The expected value.</param>
    /// <returns>The span.</returns>
    /// <exception cref="InvalidOperationException">The tag is missing or differs.</exception>
    public static Activity ShouldHaveTag(this Activity activity, string key, object? value)
    {
        ArgumentNullException.ThrowIfNull(activity);
        if (!TryGetTag(activity, key, out var actual))
        {
            throw Fail(activity, $"tag '{key}' = {Show(value)}, but the tag is missing; tags: {TagKeys(activity)}");
        }

        return Equals(actual, value)
            ? activity
            : throw Fail(activity, $"tag '{key}' = {Show(value)}, but it was {Show(actual)}");
    }

    /// <summary>Asserts the span carries exactly the tag keys <paramref name="keys"/>, in any order.</summary>
    /// <param name="activity">The span.</param>
    /// <param name="keys">The expected tag keys.</param>
    /// <returns>The span.</returns>
    /// <exception cref="InvalidOperationException">The key set differs.</exception>
    public static Activity ShouldHaveTagKeys(this Activity activity, params string[] keys)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(keys);
        var actual = activity.TagObjects.Select(t => t.Key).ToHashSet(StringComparer.Ordinal);
        var expected = keys.ToHashSet(StringComparer.Ordinal);
        if (actual.SetEquals(expected))
        {
            return activity;
        }

        var missing = expected.Except(actual).OrderBy(k => k, StringComparer.Ordinal);
        var extra = actual.Except(expected).OrderBy(k => k, StringComparer.Ordinal);
        throw Fail(
            activity,
            $"exactly the tag keys [{string.Join(", ", expected.OrderBy(k => k, StringComparer.Ordinal))}], missing [{string.Join(", ", missing)}], unexpected [{string.Join(", ", extra)}]");
    }

    /// <summary>Asserts the span does not carry the tag <paramref name="key"/>.</summary>
    /// <param name="activity">The span.</param>
    /// <param name="key">The tag key.</param>
    /// <returns>The span.</returns>
    /// <exception cref="InvalidOperationException">The tag exists.</exception>
    public static Activity ShouldNotHaveTag(this Activity activity, string key)
    {
        ArgumentNullException.ThrowIfNull(activity);
        return TryGetTag(activity, key, out var actual)
            ? throw Fail(activity, $"no tag '{key}', but it was {Show(actual)}")
            : activity;
    }

    /// <summary>Asserts the span has an event named <paramref name="name"/>.</summary>
    /// <param name="activity">The span.</param>
    /// <param name="name">The event name.</param>
    /// <returns>The span.</returns>
    /// <exception cref="InvalidOperationException">No such event.</exception>
    public static Activity ShouldHaveEvent(this Activity activity, string name)
    {
        ArgumentNullException.ThrowIfNull(activity);
        return activity.Events.Any(e => e.Name == name)
            ? activity
            : throw Fail(activity, $"an event named '{name}', but its events are [{string.Join(", ", activity.Events.Select(e => e.Name))}]");
    }

    /// <summary>Asserts <paramref name="parent"/> is the parent span (same trace, parent span id).</summary>
    /// <param name="activity">The span.</param>
    /// <param name="parent">The expected parent.</param>
    /// <returns>The span.</returns>
    /// <exception cref="InvalidOperationException">The parent differs.</exception>
    public static Activity ShouldHaveParent(this Activity activity, Activity parent)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(parent);
        return activity.TraceId == parent.TraceId && activity.ParentSpanId == parent.SpanId
            ? activity
            : throw Fail(activity, $"parent span '{parent.OperationName}' ({parent.SpanId}), but its parent span id is {activity.ParentSpanId} in trace {activity.TraceId}");
    }

    // ---- Status -----------------------------------------------------------------------------------------

    /// <summary>Asserts the span status and, when given, its description.</summary>
    /// <param name="activity">The span.</param>
    /// <param name="status">The expected status.</param>
    /// <param name="description">The expected description, or <see langword="null"/> to not check it.</param>
    /// <returns>The span.</returns>
    /// <exception cref="InvalidOperationException">The status or description differs.</exception>
    public static Activity ShouldHaveStatus(this Activity activity, ActivityStatusCode status, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(activity);
        if (activity.Status != status)
        {
            throw Fail(activity, $"status '{status}', but it was '{activity.Status}' ({Show(activity.StatusDescription)})");
        }

        return description is null || activity.StatusDescription == description
            ? activity
            : throw Fail(activity, $"status description {Show(description)}, but it was {Show(activity.StatusDescription)}");
    }

    /// <summary>Asserts the status is <see cref="ActivityStatusCode.Unset"/>.</summary>
    /// <param name="activity">The span.</param>
    /// <returns>The span.</returns>
    /// <exception cref="InvalidOperationException">The status is set.</exception>
    public static Activity ShouldBeUnset(this Activity activity) => activity.ShouldHaveStatus(ActivityStatusCode.Unset);

    // ---- The failure shapes -----------------------------------------------------------------------------

    /// <summary>
    /// Asserts the span records a failed <c>Result</c>: status <c>Error</c> described by <see cref="Error.Code"/>,
    /// <c>error.code</c> = the code, <c>error.type</c> = the <see cref="ErrorType"/> member name (<c>"_OTHER"</c> for an
    /// undefined value), and no exception event.
    /// </summary>
    /// <param name="activity">The span.</param>
    /// <param name="error">The error the operation failed with.</param>
    /// <returns>The span.</returns>
    /// <exception cref="InvalidOperationException">Any part of the shape differs.</exception>
    public static Activity ShouldBeRecordedFailure(this Activity activity, Error error)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(error);
        var problems = new List<string>();
        CheckStatus(activity, ActivityStatusCode.Error, error.Code, problems);
        CheckTag(activity, WellKnownTagKeys.ErrorCode, error.Code, problems);
        CheckTag(activity, WellKnownTagKeys.ErrorType, TelemetryShapes.ErrorTypeName(error.Type), problems);
        if (HasExceptionEvent(activity))
        {
            problems.Add("a failed Result must not add an 'exception' event");
        }

        return Verdict(activity, $"a recorded failure '{error.Code}'", problems);
    }

    /// <summary>
    /// Asserts the span records an exception: status <c>Error</c> described by the exception's full type name (never
    /// its message), <c>error.type</c> = that name, <c>error.code</c> = <paramref name="errorCode"/> when given and
    /// absent otherwise, and at least one <c>exception</c> event carrying type, message and stack trace.
    /// </summary>
    /// <param name="activity">The span.</param>
    /// <param name="exceptionType">The exact type of the exception the operation threw.</param>
    /// <param name="errorCode">The <c>IHasError</c> code, or <see langword="null"/> when the exception carries none.</param>
    /// <returns>The span.</returns>
    /// <exception cref="InvalidOperationException">Any part of the shape differs.</exception>
    public static Activity ShouldBeRecordedException(this Activity activity, Type exceptionType, string? errorCode = null)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(exceptionType);
        var typeName = exceptionType.FullName ?? exceptionType.Name;
        var problems = new List<string>();
        CheckStatus(activity, ActivityStatusCode.Error, typeName, problems);
        CheckTag(activity, WellKnownTagKeys.ErrorType, typeName, problems);
        if (errorCode is null)
        {
            if (TryGetTag(activity, WellKnownTagKeys.ErrorCode, out var code))
            {
                problems.Add($"tag '{WellKnownTagKeys.ErrorCode}' should be absent, but it was {Show(code)}");
            }
        }
        else
        {
            CheckTag(activity, WellKnownTagKeys.ErrorCode, errorCode, problems);
        }

        var events = activity.Events.Where(e => e.Name == TelemetryShapes.ExceptionEventName).ToList();
        if (events.Count == 0)
        {
            problems.Add("no 'exception' event was added");
        }
        else if (!events.Any(HasStandardExceptionTags))
        {
            problems.Add($"no 'exception' event carries all of '{TelemetryShapes.ExceptionTypeTag}', '{TelemetryShapes.ExceptionMessageTag}' and '{TelemetryShapes.ExceptionStackTraceTag}'");
        }

        return Verdict(activity, $"a recorded exception '{typeName}'", problems);
    }

    /// <summary>
    /// Asserts the span records a cancellation: status <c>Unset</c>, no <c>exception</c> event and no
    /// <c>error.*</c> tag.
    /// </summary>
    /// <param name="activity">The span.</param>
    /// <returns>The span.</returns>
    /// <exception cref="InvalidOperationException">Any part of the shape differs.</exception>
    public static Activity ShouldBeRecordedCancellation(this Activity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        var problems = new List<string>();
        CheckQuiet(activity, problems, "a cancellation");
        return Verdict(activity, "a recorded cancellation", problems);
    }

    /// <summary>
    /// Asserts the span records success: status <c>Unset</c> (never <c>Ok</c>), no <c>exception</c> event and no
    /// <c>error.*</c> tag.
    /// </summary>
    /// <param name="activity">The span.</param>
    /// <returns>The span.</returns>
    /// <exception cref="InvalidOperationException">Any part of the shape differs.</exception>
    public static Activity ShouldBeRecordedSuccess(this Activity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        var problems = new List<string>();
        CheckQuiet(activity, problems, "success");
        return Verdict(activity, "a recorded success", problems);
    }

    /// <summary>
    /// Asserts the <c>outcome</c> tag equals <paramref name="outcome"/>, which must be one of the
    /// <see cref="WellKnownOutcomes"/> values.
    /// </summary>
    /// <param name="activity">The span.</param>
    /// <param name="outcome">The expected outcome.</param>
    /// <returns>The span.</returns>
    /// <exception cref="InvalidOperationException">The expected value is not in the vocabulary, or the tag differs.</exception>
    public static Activity ShouldHaveOutcome(this Activity activity, string outcome)
    {
        ArgumentNullException.ThrowIfNull(activity);
        if (!TelemetryShapes.IsOutcome(outcome))
        {
            throw new InvalidOperationException(
                $"'{outcome}' is not an outcome; the vocabulary is {TelemetryShapes.OutcomeList}.");
        }

        return activity.ShouldHaveTag(WellKnownTagKeys.Outcome, outcome);
    }

    // ---- Hygiene and conventions ------------------------------------------------------------------------

    /// <summary>
    /// Asserts none of <paramref name="forbiddenValues"/> appears in a tag value, baggage value, the status
    /// description or an event tag value. The <c>exception</c> event is exempt: it carries the message by design.
    /// </summary>
    /// <param name="activity">The span.</param>
    /// <param name="forbiddenValues">Values (tenant ids, tokens, personal data) that must not appear.</param>
    /// <returns>The span.</returns>
    /// <exception cref="InvalidOperationException">A forbidden value was found.</exception>
    public static Activity ShouldNotLeak(this Activity activity, params string[] forbiddenValues)
    {
        ArgumentNullException.ThrowIfNull(activity);
        TelemetryShapes.ThrowIfAnyEmpty(forbiddenValues);
        var leaks = FindLeaks(activity, forbiddenValues);
        return leaks.Count == 0
            ? activity
            : throw Fail(activity, $"no forbidden value, but found:{Environment.NewLine}{string.Join(Environment.NewLine, leaks.Select(l => " - " + l))}");
    }

    /// <summary>Asserts <see cref="ShouldNotLeak(Activity, string[])"/> for every span.</summary>
    /// <param name="activities">The recorded spans.</param>
    /// <param name="forbiddenValues">Values that must not appear.</param>
    /// <returns>The same spans.</returns>
    /// <exception cref="InvalidOperationException">A forbidden value was found.</exception>
    public static IEnumerable<Activity> ShouldNotLeak(this IEnumerable<Activity> activities, params string[] forbiddenValues)
    {
        ArgumentNullException.ThrowIfNull(activities);
        TelemetryShapes.ThrowIfAnyEmpty(forbiddenValues);
        var list = activities.ToList();
        var leaks = list
            .SelectMany(a => FindLeaks(a, forbiddenValues).Select(l => $"span '{a.OperationName}': {l}"))
            .ToList();
        return leaks.Count == 0
            ? list
            : throw new InvalidOperationException(
                $"Expected no forbidden value in any span, but found:{Environment.NewLine}{string.Join(Environment.NewLine, leaks.Select(l => " - " + l))}");
    }

    /// <summary>
    /// Asserts the span follows the span conventions: source <c>SharedKernel.{Capability}</c>, a lowercase
    /// <c>{capability} {operation}</c> name, a Client/Producer/Consumer/Internal kind and lowercase dotted snake-case tag
    /// keys. Every violation is listed in the exception message.
    /// </summary>
    /// <param name="activity">The span.</param>
    /// <returns>The span.</returns>
    /// <exception cref="InvalidOperationException">At least one convention is violated.</exception>
    public static Activity ShouldFollowSpanConvention(this Activity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        var violations = SpanConventionViolations(activity);
        return violations.Count == 0
            ? activity
            : throw new InvalidOperationException(
                $"Span '{activity.OperationName}' violates {violations.Count} convention(s):{Environment.NewLine}{string.Join(Environment.NewLine, violations.Select(v => " - " + v))}");
    }

    /// <summary>Asserts <see cref="ShouldFollowSpanConvention(Activity)"/> for every span, listing all violations.</summary>
    /// <param name="activities">The recorded spans.</param>
    /// <returns>The same spans.</returns>
    /// <exception cref="InvalidOperationException">At least one convention is violated.</exception>
    public static IEnumerable<Activity> ShouldFollowSpanConvention(this IEnumerable<Activity> activities)
    {
        ArgumentNullException.ThrowIfNull(activities);
        var list = activities.ToList();
        var violations = list
            .SelectMany(a => SpanConventionViolations(a).Select(v => $"span '{a.OperationName}': {v}"))
            .ToList();
        return violations.Count == 0
            ? list
            : throw new InvalidOperationException(
                $"Spans violate {violations.Count} convention(s):{Environment.NewLine}{string.Join(Environment.NewLine, violations.Select(v => " - " + v))}");
    }

    // ---- Internals --------------------------------------------------------------------------------------

    private static List<string> SpanConventionViolations(Activity activity)
    {
        var violations = new List<string>();
        if (!TelemetryShapes.IsSpanSource(activity.Source.Name))
        {
            violations.Add($"source '{activity.Source.Name}' does not match 'SharedKernel.{{Capability}}' (^SharedKernel\\.[A-Z]\\w+$)");
        }

        if (!TelemetryShapes.IsSpanName(activity.OperationName))
        {
            violations.Add($"name '{activity.OperationName}' is not a lowercase '{{capability}} {{operation}}'");
        }

        if (activity.Kind is not (ActivityKind.Client or ActivityKind.Producer or ActivityKind.Consumer or ActivityKind.Internal))
        {
            violations.Add($"kind '{activity.Kind}' is not Client, Producer, Consumer or Internal");
        }

        foreach (var tag in activity.TagObjects)
        {
            if (!TelemetryShapes.IsTagKey(tag.Key))
            {
                violations.Add($"tag key '{tag.Key}' is not lowercase dotted snake case");
            }
        }

        return violations;
    }

    private static List<string> FindLeaks(Activity activity, string[] forbiddenValues)
    {
        var leaks = new List<string>();

        void Scan(string location, string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            foreach (var forbidden in forbiddenValues)
            {
                if (value.Contains(forbidden, StringComparison.Ordinal))
                {
                    leaks.Add($"{location} contains '{forbidden}'");
                }
            }
        }

        foreach (var tag in activity.TagObjects)
        {
            Scan($"tag '{tag.Key}'", tag.Value?.ToString());
        }

        foreach (var item in activity.Baggage)
        {
            Scan($"baggage '{item.Key}'", item.Value);
        }

        Scan("the status description", activity.StatusDescription);
        foreach (var evt in activity.Events)
        {
            if (evt.Name == TelemetryShapes.ExceptionEventName)
            {
                continue;
            }

            foreach (var tag in evt.Tags)
            {
                Scan($"event '{evt.Name}' tag '{tag.Key}'", tag.Value?.ToString());
            }
        }

        return leaks;
    }

    private static void CheckQuiet(Activity activity, List<string> problems, string what)
    {
        if (activity.Status != ActivityStatusCode.Unset)
        {
            problems.Add($"status should be 'Unset' for {what}, but it was '{activity.Status}'");
        }

        foreach (var key in new[] { WellKnownTagKeys.ErrorCode, WellKnownTagKeys.ErrorType })
        {
            if (TryGetTag(activity, key, out var value))
            {
                problems.Add($"tag '{key}' should be absent for {what}, but it was {Show(value)}");
            }
        }

        if (HasExceptionEvent(activity))
        {
            problems.Add($"{what} must not add an 'exception' event");
        }
    }

    private static void CheckStatus(Activity activity, ActivityStatusCode status, string description, List<string> problems)
    {
        if (activity.Status != status)
        {
            problems.Add($"status should be '{status}', but it was '{activity.Status}'");
        }

        if (activity.StatusDescription != description)
        {
            problems.Add($"status description should be {Show(description)}, but it was {Show(activity.StatusDescription)}");
        }
    }

    private static void CheckTag(Activity activity, string key, object expected, List<string> problems)
    {
        if (!TryGetTag(activity, key, out var actual))
        {
            problems.Add($"tag '{key}' should be {Show(expected)}, but it is missing");
        }
        else if (!Equals(actual, expected))
        {
            problems.Add($"tag '{key}' should be {Show(expected)}, but it was {Show(actual)}");
        }
    }

    private static bool HasExceptionEvent(Activity activity) =>
        activity.Events.Any(e => e.Name == TelemetryShapes.ExceptionEventName);

    private static bool HasStandardExceptionTags(ActivityEvent evt)
    {
        var keys = evt.Tags.Select(t => t.Key).ToHashSet(StringComparer.Ordinal);
        return keys.Contains(TelemetryShapes.ExceptionTypeTag)
            && keys.Contains(TelemetryShapes.ExceptionMessageTag)
            && keys.Contains(TelemetryShapes.ExceptionStackTraceTag);
    }

    private static Activity Verdict(Activity activity, string expected, List<string> problems) =>
        problems.Count == 0
            ? activity
            : throw new InvalidOperationException(
                $"Expected span '{activity.OperationName}' to be {expected}, but:{Environment.NewLine}{string.Join(Environment.NewLine, problems.Select(p => " - " + p))}");

    private static bool TryGetTag(Activity activity, string key, out object? value)
    {
        foreach (var tag in activity.TagObjects)
        {
            if (tag.Key == key)
            {
                value = tag.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static string TagKeys(Activity activity) => $"[{string.Join(", ", activity.TagObjects.Select(t => t.Key))}]";

    private static string Show(object? value) =>
        value is null ? "null" : $"'{value}' ({value.GetType().Name})";

    private static string Describe(List<Activity> activities) =>
        activities.Count == 0 ? "no spans" : $"[{string.Join(", ", activities.Select(a => $"'{a.OperationName}'"))}]";

    private static InvalidOperationException Fail(Activity activity, string expected) =>
        new($"Expected span '{activity.OperationName}' to have {expected}.");
}
