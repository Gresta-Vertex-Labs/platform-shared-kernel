using SharedKernel.Testing.Security;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Security;

/// <summary>
/// Proves <see cref="ApiKeyRotationScenarioBuilder"/>'s intra-package scenario-generation surface.
/// </summary>
/// <remarks>
/// <para>
/// DEFERRED: the end-to-end interop half of T-84 (calling the real
/// <c>SharedKernel.Security.ApiKey.ApiKeyRotationComparer.AnyMatch</c> against this builder's output) is
/// NOT covered here. As of this pass, <c>12.Security</c>'s <c>SharedKernel.Security.ApiKey</c> package
/// ships no <c>ApiKeyRotationComparer</c> type (verified directly against every <c>.cs</c> file under
/// <c>12.Security/SharedKernel.Security.ApiKey/</c> — only <c>ApiKeyAuthenticationHandler</c>,
/// <c>ApiKeyClaimTypes</c>, <c>ApiKeyUserContext</c>, <c>ApiKeyValidationResult</c>,
/// <c>ConstantTimeKeyComparer</c>, and <c>IApiKeyValidator</c> exist under <c>Validation/</c>; root
/// <c>state-map.md</c>'s P-389 is still <c>◐</c> Dispatched, unimplemented). This mirrors the
/// <c>Search/</c> T-75 intra-package-only precedent (P-355/WO-055): the scenario-generation half proceeds
/// unblocked below; the interop half becomes a future session's work once <c>ApiKeyRotationComparer</c>
/// ships.
/// </para>
/// </remarks>
public sealed class ApiKeyRotationScenarioBuilderTests
{
    [Fact]
    public void Build_Candidates_IsOldKeyThenNewKeyThenExtraCandidates_InCallOrder()
    {
        var scenario = new ApiKeyRotationScenarioBuilder()
            .WithExtraCandidate("extra-1")
            .WithExtraCandidate("extra-2")
            .Build();

        Assert.Equal(
            [scenario.OldKey, scenario.NewKey, "extra-1", "extra-2"],
            scenario.Candidates);
    }

    [Fact]
    public void Build_NoExtraCandidates_CandidatesIsExactlyOldKeyThenNewKey()
    {
        var scenario = new ApiKeyRotationScenarioBuilder().Build();

        Assert.Equal([scenario.OldKey, scenario.NewKey], scenario.Candidates);
    }

    [Fact]
    public void Build_NeverValidKey_IsAbsentFromCandidates()
    {
        var scenario = new ApiKeyRotationScenarioBuilder()
            .WithExtraCandidate("extra-1")
            .Build();

        Assert.DoesNotContain(scenario.NeverValidKey, scenario.Candidates);
    }

    [Fact]
    public void Build_DefaultOldAndNewKeys_AreDistinctAndDeterministicallyFormatted()
    {
        var scenario = new ApiKeyRotationScenarioBuilder().Build();

        Assert.NotEqual(scenario.OldKey, scenario.NewKey);
        Assert.StartsWith("apikey-test-old-", scenario.OldKey);
        Assert.StartsWith("apikey-test-new-", scenario.NewKey);
        Assert.StartsWith("apikey-test-never-valid-", scenario.NeverValidKey);
    }

    [Fact]
    public void WithOldKey_ExplicitValue_IsHonored()
    {
        var scenario = new ApiKeyRotationScenarioBuilder().WithOldKey("explicit-old").Build();

        Assert.Equal("explicit-old", scenario.OldKey);
        Assert.Equal("explicit-old", scenario.Candidates[0]);
    }

    [Fact]
    public void WithOldKey_NullResetsToDeterministicDefault()
    {
        var scenario = new ApiKeyRotationScenarioBuilder()
            .WithOldKey("explicit-old")
            .WithOldKey(null)
            .Build();

        Assert.NotEqual("explicit-old", scenario.OldKey);
        Assert.StartsWith("apikey-test-old-", scenario.OldKey);
    }

    [Fact]
    public void WithNewKey_ExplicitValue_IsHonored()
    {
        var scenario = new ApiKeyRotationScenarioBuilder().WithNewKey("explicit-new").Build();

        Assert.Equal("explicit-new", scenario.NewKey);
        Assert.Equal("explicit-new", scenario.Candidates[1]);
    }

    [Fact]
    public void WithNewKey_NullResetsToDeterministicDefault()
    {
        var scenario = new ApiKeyRotationScenarioBuilder()
            .WithNewKey("explicit-new")
            .WithNewKey(null)
            .Build();

        Assert.NotEqual("explicit-new", scenario.NewKey);
        Assert.StartsWith("apikey-test-new-", scenario.NewKey);
    }

    [Fact]
    public void TwoSeparatelyConstructedBuilders_NeverProduceCollidingKeyStrings()
    {
        var scenarioA = new ApiKeyRotationScenarioBuilder().Build();
        var scenarioB = new ApiKeyRotationScenarioBuilder().Build();

        Assert.NotEqual(scenarioA.OldKey, scenarioB.OldKey);
        Assert.NotEqual(scenarioA.NewKey, scenarioB.NewKey);
        Assert.NotEqual(scenarioA.NeverValidKey, scenarioB.NeverValidKey);
        Assert.Empty(scenarioA.Candidates.Intersect(scenarioB.Candidates));
    }
}
