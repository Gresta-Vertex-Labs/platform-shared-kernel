namespace SharedKernel.Testing.Security;

/// <summary>
/// Fluent builder that constructs a deterministic old/new/extra-candidate API-key rotation scenario for
/// use as an input fixture in a consuming service's own key-rotation test.
/// </summary>
/// <remarks>
/// A PURE scenario/data builder — NEVER calls the real
/// <c>SharedKernel.Security.ApiKey.ApiKeyRotationComparer.AnyMatch</c> itself, so this type takes ZERO
/// <c>ProjectReference</c> to <c>SharedKernel.Security.ApiKey</c>. A consuming test's own
/// <c>.ApiKey</c>-referencing test project calls <c>AnyMatch(scenario.NewKey, scenario.Candidates)</c>
/// (or similar) directly against this builder's output.
/// </remarks>
public sealed class ApiKeyRotationScenarioBuilder
{
    private static int _sequence;

    private readonly int _instanceSequence = Interlocked.Increment(ref _sequence);
    private readonly List<string> _extraCandidates = [];

    private string _oldKey;
    private string _newKey;

    /// <summary>Initializes a new <see cref="ApiKeyRotationScenarioBuilder"/> with deterministic default keys.</summary>
    public ApiKeyRotationScenarioBuilder()
    {
        _oldKey = FormatKey("old");
        _newKey = FormatKey("new");
    }

    /// <summary>
    /// Sets the old (pre-rotation) key. Omitting <paramref name="key"/> (or passing <see langword="null"/>)
    /// resets it to a deterministic per-instance incrementing test-key string — never
    /// <see cref="Guid.NewGuid"/>/real randomness.
    /// </summary>
    /// <returns>This instance, for fluent chaining.</returns>
    public ApiKeyRotationScenarioBuilder WithOldKey(string? key = null)
    {
        _oldKey = key ?? FormatKey("old");
        return this;
    }

    /// <summary>
    /// Sets the new (post-rotation) key. Omitting <paramref name="key"/> (or passing
    /// <see langword="null"/>) resets it to the same determinism convention as <see cref="WithOldKey"/>.
    /// </summary>
    /// <returns>This instance, for fluent chaining.</returns>
    public ApiKeyRotationScenarioBuilder WithNewKey(string? key = null)
    {
        _newKey = key ?? FormatKey("new");
        return this;
    }

    /// <summary>
    /// Adds an additional simultaneously-valid candidate key, supporting
    /// more-than-two-simultaneously-valid-key scenarios. Additive.
    /// </summary>
    /// <returns>This instance, for fluent chaining.</returns>
    public ApiKeyRotationScenarioBuilder WithExtraCandidate(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        _extraCandidates.Add(key);
        return this;
    }

    /// <summary>Builds the configured <see cref="ApiKeyRotationScenario"/>.</summary>
    public ApiKeyRotationScenario Build()
    {
        var candidates = new List<string>(2 + _extraCandidates.Count) { _oldKey, _newKey };
        candidates.AddRange(_extraCandidates);

        return new ApiKeyRotationScenario(_oldKey, _newKey, candidates, FormatKey("never-valid"));
    }

    private string FormatKey(string label) => $"apikey-test-{label}-{_instanceSequence:D6}";
}

/// <summary>Return type of <see cref="ApiKeyRotationScenarioBuilder.Build"/>.</summary>
/// <param name="OldKey">The pre-rotation key.</param>
/// <param name="NewKey">The post-rotation key.</param>
/// <param name="Candidates">
/// <c>[OldKey, NewKey, ...ExtraCandidates]</c> in call order — the exact shape
/// <c>ApiKeyRotationComparer.AnyMatch</c>'s second parameter expects.
/// </param>
/// <param name="NeverValidKey">
/// A deterministic key guaranteed absent from <see cref="Candidates"/>, for the negative
/// "presented key matches nothing" test case.
/// </param>
public sealed record ApiKeyRotationScenario(
    string OldKey,
    string NewKey,
    IReadOnlyList<string> Candidates,
    string NeverValidKey);
