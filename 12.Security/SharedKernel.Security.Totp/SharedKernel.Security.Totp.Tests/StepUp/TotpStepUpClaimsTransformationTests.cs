using System.Security.Claims;
using Microsoft.Extensions.Logging;
using SharedKernel.Security.Abstractions.Claims;
using SharedKernel.Security.Oidc.Mapping;
using SharedKernel.Security.Oidc.Options;
using SharedKernel.Security.Totp.Challenge;
using SharedKernel.Security.Totp.StepUp;
using SharedKernel.Security.Totp.Tests.Challenge;
using SharedKernel.Testing.Logging;
using Xunit;

namespace SharedKernel.Security.Totp.Tests.StepUp;

public sealed class TotpStepUpClaimsTransformationTests
{
    private readonly FakeTotpChallengeStore _challengeStore = new();
    private readonly TotpStepUpOptions _options = new();
    private readonly InMemoryLogger<TotpStepUpClaimsTransformation> _logger = new();
    private readonly TotpStepUpClaimsTransformation _sut;

    public TotpStepUpClaimsTransformationTests()
    {
        _sut = new TotpStepUpClaimsTransformation(_challengeStore, _options, _logger);
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenAnyArgumentIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new TotpStepUpClaimsTransformation(null!, _options, _logger));
        Assert.Throws<ArgumentNullException>(() => new TotpStepUpClaimsTransformation(_challengeStore, null!, _logger));
        Assert.Throws<ArgumentNullException>(() => new TotpStepUpClaimsTransformation(_challengeStore, _options, null!));
    }

    [Fact]
    public async Task TransformAsync_AnonymousPrincipal_SkippedWithoutAnyStoreCall()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        var result = await _sut.TransformAsync(principal);

        Assert.Same(principal, result);
        Assert.Equal(0, _challengeStore.TryGetCallCount);
    }

    [Fact]
    public async Task TransformAsync_AuthenticatedPrincipalWithNoSubjectClaim_SkippedWithoutAnyStoreCall()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([], "Bearer"));

        var result = await _sut.TransformAsync(principal);

        Assert.Same(principal, result);
        Assert.Equal(0, _challengeStore.TryGetCallCount);
    }

    [Fact]
    public async Task TransformAsync_AuthenticatedPrincipalWithUnparseableSubjectClaim_SkippedWithoutAnyStoreCall()
    {
        var principal = AuthenticatedPrincipal("not-a-guid");

        var result = await _sut.TransformAsync(principal);

        Assert.Same(principal, result);
        Assert.Equal(0, _challengeStore.TryGetCallCount);
    }

    [Fact]
    public async Task TransformAsync_FreshSuccessfulChallenge_GainsTheConfiguredAmrClaim()
    {
        var userId = Guid.NewGuid();
        await SeedChallengeAsync(userId, DateTimeOffset.UtcNow);
        var principal = AuthenticatedPrincipal(userId.ToString());

        var result = await _sut.TransformAsync(principal);

        Assert.Contains(result.Claims, c => c.Type == _options.AmrClaimType && c.Value == _options.AmrValue);
    }

    [Fact]
    public async Task TransformAsync_StaleChallenge_DoesNotGainTheAmrClaim()
    {
        var userId = Guid.NewGuid();
        await SeedChallengeAsync(userId, DateTimeOffset.UtcNow - _options.ChallengeFreshnessWindow - TimeSpan.FromMinutes(1));
        var principal = AuthenticatedPrincipal(userId.ToString());

        var result = await _sut.TransformAsync(principal);

        Assert.DoesNotContain(result.Claims, c => c.Type == _options.AmrClaimType);
    }

    [Fact]
    public async Task TransformAsync_NoChallengeRecorded_DoesNotGainTheAmrClaim()
    {
        var principal = AuthenticatedPrincipal(Guid.NewGuid().ToString());

        var result = await _sut.TransformAsync(principal);

        Assert.DoesNotContain(result.Claims, c => c.Type == _options.AmrClaimType);
    }

    [Fact]
    public async Task TransformAsync_CalledTwice_DoesNotAddADuplicateAmrClaim()
    {
        var userId = Guid.NewGuid();
        await SeedChallengeAsync(userId, DateTimeOffset.UtcNow);
        var principal = AuthenticatedPrincipal(userId.ToString());

        var firstResult = await _sut.TransformAsync(principal);
        var secondResult = await _sut.TransformAsync(firstResult);

        Assert.Single(secondResult.Claims, c => c.Type == _options.AmrClaimType && c.Value == _options.AmrValue);
    }

    [Fact]
    public async Task TransformAsync_NeverMutatesTheOriginalClaimsIdentityInstance()
    {
        var userId = Guid.NewGuid();
        await SeedChallengeAsync(userId, DateTimeOffset.UtcNow);
        var principal = AuthenticatedPrincipal(userId.ToString());
        var originalIdentity = principal.Identity;
        var originalClaimCount = principal.Claims.Count();

        var result = await _sut.TransformAsync(principal);

        Assert.Same(originalIdentity, principal.Identity);
        Assert.Equal(originalClaimCount, principal.Claims.Count());
        Assert.NotSame(principal, result);
    }

    [Fact]
    public async Task TransformAsync_FreshChallenge_LogsTotpStepUpClaimApplied()
    {
        var userId = Guid.NewGuid();
        await SeedChallengeAsync(userId, DateTimeOffset.UtcNow);
        var principal = AuthenticatedPrincipal(userId.ToString());

        _ = await _sut.TransformAsync(principal);

        _logger.Records.ShouldHaveLogged(new EventId(12401), LogLevel.Debug);
    }

    [Fact]
    public async Task TransformAsync_InteropWithRealOidcUserContext_WasAuthenticatedWithReflectsTheStampedClaim()
    {
        var userId = Guid.NewGuid();
        await SeedChallengeAsync(userId, DateTimeOffset.UtcNow);
        var principal = AuthenticatedPrincipal(userId.ToString());

        var transformed = await _sut.TransformAsync(principal);

        var userContext = new OidcUserContext(transformed, new ClaimMappingOptions());

        Assert.True(userContext.WasAuthenticatedWith("otp"));
        Assert.Contains("otp", userContext.AuthenticationMethods);
    }

    private async Task SeedChallengeAsync(Guid userId, DateTimeOffset verifiedAt)
    {
        string identityKey = TotpIdentityKeyFormatter.Format(userId);
        await _challengeStore.RecordSuccessfulChallengeAsync(identityKey, verifiedAt);
    }

    private static ClaimsPrincipal AuthenticatedPrincipal(string subjectValue) =>
        new(new ClaimsIdentity([new Claim(SecurityClaimTypes.UserId, subjectValue)], "Bearer"));
}
