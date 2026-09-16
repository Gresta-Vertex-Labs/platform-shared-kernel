using SharedKernel.Security.ApiKey.Samples;
using SharedKernel.Security.ApiKey.Validation;
using Xunit;

namespace SharedKernel.Security.ApiKey.Tests.Validation;

/// <summary>
/// Proves the rotation-window recipe (<see cref="RotationWindowApiKeyValidatorSample"/>, built on
/// <c>SharedKernel.Cryptography.FixedTimeComparison.AreEqualToAny</c>) accepts every simultaneously-active
/// key for a client and rejects a retired one. That every candidate is always compared, never
/// short-circuited, is a guarantee of <c>FixedTimeComparison.AreEqualToAny</c> itself and is proven in
/// <c>01.Core</c>'s own test suite.
/// </summary>
public sealed class ApiKeyRotationComparisonTests
{
    private const string OldKey = "old-key-still-in-grace-window";
    private const string NewKey = "new-rotated-key";

    private static RotationWindowApiKeyValidatorSample CreateValidator() =>
        new(new Dictionary<string, IReadOnlyList<string>>
        {
            ["client-1"] = [NewKey, OldKey],
            ["client-2"] = ["client-2-only-key"],
        });

    [Fact]
    public async Task RotationWindow_OldKeyStillValid_AuthenticatesOwningClient()
    {
        var result = await CreateValidator().ValidateAsync(OldKey, CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal("client-1", result.ClientId);
    }

    [Fact]
    public async Task RotationWindow_NewKeyValid_AuthenticatesOwningClient()
    {
        var result = await CreateValidator().ValidateAsync(NewKey, CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal("client-1", result.ClientId);
    }

    [Fact]
    public async Task RotationWindow_KeyOfAnotherClient_AuthenticatesThatClient()
    {
        var result = await CreateValidator().ValidateAsync("client-2-only-key", CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal("client-2", result.ClientId);
    }

    [Theory]
    [InlineData("a-fully-revoked-key-from-before-the-window")]
    [InlineData("new-rotated-keY")]
    [InlineData("new-rotated-key-with-suffix")]
    [InlineData("new-rotated")]
    public async Task RotationWindow_RevokedOrNearMissKey_IsRejected(string presentedKey)
    {
        var result = await CreateValidator().ValidateAsync(presentedKey, CancellationToken.None);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task EmptyCandidateSet_RejectsEveryKey()
    {
        var validator = new RotationWindowApiKeyValidatorSample(
            new Dictionary<string, IReadOnlyList<string>> { ["client-1"] = [] });

        var result = await validator.ValidateAsync(NewKey, CancellationToken.None);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task NullPresentedKey_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => CreateValidator().ValidateAsync(null!, CancellationToken.None));
    }
}
