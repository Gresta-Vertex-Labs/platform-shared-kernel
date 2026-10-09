using SharedKernel.Primitives.Propagation;

namespace SharedKernel.Testing.Telemetry;

/// <summary>Read-only assertions over a <see cref="MetricRecorder"/>.</summary>
/// <remarks>
/// Every member throws <see cref="InvalidOperationException"/> with a readable message and returns the recorder so
/// assertions chain. Tag filters are subsets: a measurement matches when it carries every given tag.
/// </remarks>
public static class MetricAssertions
{
    /// <summary>Asserts an instrument named <paramref name="name"/> was published, optionally with a unit and kind.</summary>
    /// <param name="recorder">The recorder.</param>
    /// <param name="name">The instrument name.</param>
    /// <param name="unit">The expected unit, or <see langword="null"/> to not check it.</param>
    /// <param name="kind">The expected kind, or <see langword="null"/> to not check it.</param>
    /// <returns>The recorder.</returns>
    /// <exception cref="InvalidOperationException">The instrument is missing or differs.</exception>
    public static MetricRecorder ShouldHaveInstrument(
        this MetricRecorder recorder,
        string name,
        string? unit = null,
        InstrumentKind? kind = null)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        var matches = recorder.Instruments.Where(i => i.Name == name).ToList();
        if (matches.Count == 0)
        {
            throw new InvalidOperationException(
                $"Expected an instrument named '{name}', but found [{string.Join(", ", recorder.Instruments.Select(i => $"'{i.Name}'"))}].");
        }

        var problems = new List<string>();
        foreach (var instrument in matches)
        {
            problems.Clear();
            if (unit is not null && instrument.Unit != unit)
            {
                problems.Add($"unit should be '{unit}', but it was '{instrument.Unit}'");
            }

            if (kind is not null && instrument.Kind != kind)
            {
                problems.Add($"kind should be '{kind}', but it was '{instrument.Kind}'");
            }

            if (problems.Count == 0)
            {
                return recorder;
            }
        }

        throw new InvalidOperationException(
            $"Expected instrument '{name}' to match, but {string.Join("; ", problems)}.");
    }

    /// <summary>
    /// Asserts at least one measurement of <paramref name="instrument"/> carries every tag in <paramref name="tags"/>
    /// and, when given, equals <paramref name="value"/>.
    /// </summary>
    /// <param name="recorder">The recorder.</param>
    /// <param name="instrument">The instrument name.</param>
    /// <param name="value">The expected value, or <see langword="null"/> to not check it.</param>
    /// <param name="tags">A tag subset the measurement must contain.</param>
    /// <returns>The recorder.</returns>
    /// <exception cref="InvalidOperationException">No measurement matches.</exception>
    public static MetricRecorder ShouldHaveMeasurement(
        this MetricRecorder recorder,
        string instrument,
        double? value = null,
        params KeyValuePair<string, object?>[] tags)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        var candidates = recorder.Matching(instrument, tags).ToList();
        if (candidates.Any(m => value is null || m.Value.Equals(value.Value)))
        {
            return recorder;
        }

        var all = recorder.MeasurementsOf(instrument);
        throw new InvalidOperationException(
            $"Expected a measurement of '{instrument}'{(value is null ? string.Empty : $" = {value}")}{DescribeTags(tags)}, "
            + $"but found {(all.Count == 0 ? "no measurements" : $"[{string.Join("; ", all.Select(Describe))}]")}.");
    }

    /// <summary>Asserts every measurement of <paramref name="instrument"/> carries exactly the tag keys <paramref name="keys"/>.</summary>
    /// <param name="recorder">The recorder.</param>
    /// <param name="instrument">The instrument name.</param>
    /// <param name="keys">The expected tag keys, in any order.</param>
    /// <returns>The recorder.</returns>
    /// <exception cref="InvalidOperationException">There is no measurement, or a key set differs.</exception>
    public static MetricRecorder ShouldHaveTagSet(this MetricRecorder recorder, string instrument, params string[] keys)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        ArgumentNullException.ThrowIfNull(keys);
        var measurements = RequireMeasurements(recorder, instrument);
        var expected = keys.ToHashSet(StringComparer.Ordinal);
        var offenders = measurements
            .Where(m => !m.Tags.Select(t => t.Key).ToHashSet(StringComparer.Ordinal).SetEquals(expected))
            .Select(m => $"[{string.Join(", ", m.Tags.Select(t => t.Key))}]")
            .Distinct()
            .ToList();
        return offenders.Count == 0
            ? recorder
            : throw new InvalidOperationException(
                $"Expected every measurement of '{instrument}' to carry exactly the tag keys [{string.Join(", ", expected.OrderBy(k => k, StringComparer.Ordinal))}], but found key sets {string.Join(", ", offenders)}.");
    }

    /// <summary>Asserts no measurement of <paramref name="instrument"/> carries the tag <paramref name="key"/>.</summary>
    /// <param name="recorder">The recorder.</param>
    /// <param name="instrument">The instrument name.</param>
    /// <param name="key">The forbidden tag key.</param>
    /// <returns>The recorder.</returns>
    /// <exception cref="InvalidOperationException">A measurement carries the tag.</exception>
    public static MetricRecorder ShouldNotHaveTagKey(this MetricRecorder recorder, string instrument, string key)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        var count = recorder.MeasurementsOf(instrument).Count(m => m.TryGetTag(key, out _));
        return count == 0
            ? recorder
            : throw new InvalidOperationException(
                $"Expected no measurement of '{instrument}' to carry the tag '{key}', but {count} did.");
    }

    /// <summary>Asserts the sum of the matching measurements of <paramref name="instrument"/> equals <paramref name="expected"/>.</summary>
    /// <param name="recorder">The recorder.</param>
    /// <param name="instrument">The instrument name.</param>
    /// <param name="expected">The expected sum.</param>
    /// <param name="tags">A tag subset the measurements must contain.</param>
    /// <returns>The recorder.</returns>
    /// <exception cref="InvalidOperationException">The sum differs.</exception>
    public static MetricRecorder ShouldHaveSum(
        this MetricRecorder recorder,
        string instrument,
        double expected,
        params KeyValuePair<string, object?>[] tags)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        var actual = recorder.Sum(instrument, tags);
        return actual.Equals(expected)
            ? recorder
            : throw new InvalidOperationException(
                $"Expected the sum of '{instrument}'{DescribeTags(tags)} to be {expected}, but it was {actual} over {recorder.Count(instrument, tags)} measurement(s).");
    }

    /// <summary>
    /// Asserts every <c>outcome</c> tag value of <paramref name="instrument"/> is one of the
    /// <see cref="WellKnownOutcomes"/> values, and that at least one measurement carries the tag.
    /// </summary>
    /// <param name="recorder">The recorder.</param>
    /// <param name="instrument">The instrument name.</param>
    /// <returns>The recorder.</returns>
    /// <exception cref="InvalidOperationException">A value is outside the vocabulary, or no measurement carries the tag.</exception>
    public static MetricRecorder ShouldUseOutcomeVocabulary(this MetricRecorder recorder, string instrument)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        var values = new List<object?>();
        foreach (var measurement in recorder.MeasurementsOf(instrument))
        {
            if (measurement.TryGetTag(WellKnownTagKeys.Outcome, out var value))
            {
                values.Add(value);
            }
        }

        if (values.Count == 0)
        {
            throw new InvalidOperationException(
                $"Expected measurements of '{instrument}' to carry the '{WellKnownTagKeys.Outcome}' tag, but none did.");
        }

        var invalid = values
            .Where(v => v is not string s || !TelemetryShapes.IsOutcome(s))
            .Select(v => v is null ? "null" : $"'{v}'")
            .Distinct()
            .ToList();
        return invalid.Count == 0
            ? recorder
            : throw new InvalidOperationException(
                $"Expected every '{WellKnownTagKeys.Outcome}' value of '{instrument}' to be one of {TelemetryShapes.OutcomeList}, but found {string.Join(", ", invalid)}.");
    }

    /// <summary>Asserts none of <paramref name="forbiddenValues"/> appears in a tag value of any measurement.</summary>
    /// <param name="recorder">The recorder.</param>
    /// <param name="forbiddenValues">Values (tenant ids, tokens, personal data) that must not appear.</param>
    /// <returns>The recorder.</returns>
    /// <exception cref="InvalidOperationException">A forbidden value was found.</exception>
    public static MetricRecorder ShouldNotLeak(this MetricRecorder recorder, params string[] forbiddenValues)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        TelemetryShapes.ThrowIfAnyEmpty(forbiddenValues);
        var leaks = new List<string>();
        foreach (var measurement in recorder.Measurements)
        {
            foreach (var tag in measurement.Tags)
            {
                var text = tag.Value?.ToString();
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                foreach (var forbidden in forbiddenValues)
                {
                    if (text.Contains(forbidden, StringComparison.Ordinal))
                    {
                        leaks.Add($"'{measurement.InstrumentName}' tag '{tag.Key}' contains '{forbidden}'");
                    }
                }
            }
        }

        return leaks.Count == 0
            ? recorder
            : throw new InvalidOperationException(
                $"Expected no forbidden value in any metric tag, but found:{Environment.NewLine}{string.Join(Environment.NewLine, leaks.Distinct().Select(l => " - " + l))}");
    }

    /// <summary>
    /// Asserts the recorded metrics follow the metric conventions: meter <c>SharedKernel.{Capability}</c>, instrument
    /// <c>sharedkernel.{capability}.*</c> in lowercase, a non-empty unit on every instrument, duration histograms in
    /// seconds (<c>s</c>) and lowercase dotted snake-case tag keys. Every violation is listed in the exception message.
    /// </summary>
    /// <param name="recorder">The recorder.</param>
    /// <returns>The recorder.</returns>
    /// <exception cref="InvalidOperationException">At least one convention is violated.</exception>
    public static MetricRecorder ShouldFollowMetricConvention(this MetricRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        var violations = new List<string>();
        foreach (var instrument in recorder.Instruments)
        {
            if (!TelemetryShapes.IsMeterName(instrument.MeterName))
            {
                violations.Add($"meter '{instrument.MeterName}' does not match 'SharedKernel.{{Capability}}' (^SharedKernel\\.[A-Z]\\w+$)");
            }

            if (!TelemetryShapes.IsInstrumentName(instrument.Name))
            {
                violations.Add($"instrument '{instrument.Name}' is not a lowercase 'sharedkernel.{{capability}}.*' name");
            }

            if (string.IsNullOrEmpty(instrument.Unit))
            {
                violations.Add($"instrument '{instrument.Name}' declares no unit");
            }
            else if (instrument.Kind == InstrumentKind.Histogram
                     && instrument.Name.Contains("duration", StringComparison.Ordinal)
                     && instrument.Unit != "s")
            {
                violations.Add($"duration histogram '{instrument.Name}' uses unit '{instrument.Unit}' instead of 's'");
            }
        }

        foreach (var measurement in recorder.Measurements)
        {
            foreach (var tag in measurement.Tags)
            {
                if (!TelemetryShapes.IsTagKey(tag.Key))
                {
                    violations.Add($"instrument '{measurement.InstrumentName}' tag key '{tag.Key}' is not lowercase dotted snake case");
                }
            }
        }

        return violations.Count == 0
            ? recorder
            : throw new InvalidOperationException(
                $"Metrics violate {violations.Distinct().Count()} convention(s):{Environment.NewLine}{string.Join(Environment.NewLine, violations.Distinct().Select(v => " - " + v))}");
    }

    private static List<RecordedMeasurement> RequireMeasurements(MetricRecorder recorder, string instrument)
    {
        var measurements = recorder.MeasurementsOf(instrument).ToList();
        return measurements.Count > 0
            ? measurements
            : throw new InvalidOperationException($"Expected measurements of '{instrument}', but none were recorded.");
    }

    private static string DescribeTags(KeyValuePair<string, object?>[] tags) =>
        tags.Length == 0 ? string.Empty : $" with tags [{string.Join(", ", tags.Select(t => $"{t.Key}={t.Value ?? "null"}"))}]";

    private static string Describe(RecordedMeasurement measurement) =>
        $"{measurement.Value} {{{string.Join(", ", measurement.Tags.Select(t => $"{t.Key}={t.Value ?? "null"}"))}}}";
}
