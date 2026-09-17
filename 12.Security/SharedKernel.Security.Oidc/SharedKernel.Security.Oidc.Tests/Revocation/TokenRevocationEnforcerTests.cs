using Microsoft.IdentityModel.JsonWebTokens;
using SharedKernel.Security.Abstractions;
using SharedKernel.Security.Oidc.Options;
using SharedKernel.Security.Oidc.Revocation;
using SharedKernel.Security.Oidc.Tests.Infrastructure;
using SharedKernel.Testing.Logging;
using Xunit;

namespace SharedKernel.Security.Oidc.Tests.Revocation;

public sealed class TokenRevocationEnforcerTests
{
    private static readonly DateTime Expiry = new(2030, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly RecordingRevocationCheck _check = new();
    private readonly RecordingRevocationCache _cache = new();
    private readonly InMemoryLogger _logger = new();
    private readonly TokenRevocationOptions _options = new();
    private readonly JsonWebToken _token = new(TestTokens.Create()
        .WithClaim("jti", "jti-1")
        .WithLifetime(Expiry.AddHours(-1), Expiry)
        .Build());

    private string Hash => TestTokens.Sha256Base64Url(_token.EncodedToken);

    [Fact]
    public async Task IsRevokedAsync_NoCache_ReturnsCheckAnswer()
    {
        _check.IsRevoked = _ => true;
        var enforcer = new TokenRevocationEnforcer(_check);

        Assert.True(await IsRevokedAsync(enforcer, Expiry.AddMinutes(-10)));
    }

    [Fact]
    public async Task IsRevokedAsync_Request_CarriesUserSubjectClientAndSession()
    {
        var enforcer = new TokenRevocationEnforcer(_check);
        var user = new UserContext(IdentityKind.User, "context-subject") { ClientId = "client-1", SessionId = "session-1" };

        await enforcer.IsRevokedAsync(_token, user, _options, Expiry.AddMinutes(-10), _logger, CancellationToken.None);

        TokenRevocationRequest request = Assert.Single(_check.Requests);
        Assert.Equal("client-1", request.ClientId);
        Assert.Equal("session-1", request.SessionId);
        Assert.Equal("context-subject", request.SubjectId);
        Assert.Equal("jti-1", request.TokenId);
        Assert.Equal(Hash, request.TokenHash);
        Assert.Equal(new DateTimeOffset(Expiry), request.ExpiresAt);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IsRevokedAsync_CacheHit_ReturnsCachedAnswerWithoutCheck(bool cached)
    {
        _cache.Seed(Hash, cached);
        _check.IsRevoked = _ => !cached;
        var enforcer = new TokenRevocationEnforcer(_check, _cache);

        bool revoked = await IsRevokedAsync(enforcer, Expiry.AddMinutes(-10));

        Assert.Equal(cached, revoked);
        Assert.Empty(_check.Requests);
        Assert.Empty(_cache.Writes);
    }

    [Fact]
    public async Task IsRevokedAsync_CachedRevoked_Logs()
    {
        _cache.Seed(Hash, isRevoked: true);

        await IsRevokedAsync(new TokenRevocationEnforcer(_check, _cache), Expiry.AddMinutes(-10));

        _logger.Records.ShouldHaveLoggedWithProperty(new(12103), "CheckAvailable", true);
    }

    [Fact]
    public async Task IsRevokedAsync_RevokedMiss_CachesUntilTokenExpiry()
    {
        _check.IsRevoked = _ => true;

        bool revoked = await IsRevokedAsync(new TokenRevocationEnforcer(_check, _cache), Expiry.AddMinutes(-10));

        Assert.True(revoked);
        Assert.Equal((Hash, true, new DateTimeOffset(Expiry)), Assert.Single(_cache.Writes));
    }

    [Fact]
    public async Task IsRevokedAsync_NotRevokedMiss_CachesForConfiguredDuration()
    {
        DateTimeOffset now = new(Expiry.AddMinutes(-10));

        bool revoked = await IsRevokedAsync(new TokenRevocationEnforcer(_check, _cache), now);

        Assert.False(revoked);
        Assert.Equal((Hash, false, now.AddSeconds(30)), Assert.Single(_cache.Writes));
    }

    [Fact]
    public async Task IsRevokedAsync_NotRevokedNearExpiry_CachesOnlyUntilExpiry()
    {
        DateTimeOffset now = new(Expiry.AddSeconds(-10));

        await IsRevokedAsync(new TokenRevocationEnforcer(_check, _cache), now);

        Assert.Equal(new DateTimeOffset(Expiry), Assert.Single(_cache.Writes).ExpiresAt);
    }

    [Fact]
    public async Task IsRevokedAsync_ZeroNotRevokedDuration_DoesNotCacheNotRevoked()
    {
        _options.NotRevokedCacheDuration = TimeSpan.Zero;

        await IsRevokedAsync(new TokenRevocationEnforcer(_check, _cache), Expiry.AddMinutes(-10));

        Assert.Empty(_cache.Writes);
    }

    [Fact]
    public async Task IsRevokedAsync_ZeroNotRevokedDuration_StillCachesRevoked()
    {
        _options.NotRevokedCacheDuration = TimeSpan.Zero;
        _check.IsRevoked = _ => true;

        await IsRevokedAsync(new TokenRevocationEnforcer(_check, _cache), Expiry.AddMinutes(-10));

        Assert.True(Assert.Single(_cache.Writes).IsRevoked);
    }

    [Fact]
    public async Task IsRevokedAsync_TokenAlreadyExpired_DoesNotCache()
    {
        _check.IsRevoked = _ => true;

        await IsRevokedAsync(new TokenRevocationEnforcer(_check, _cache), Expiry.AddSeconds(5));

        Assert.Empty(_cache.Writes);
    }

    [Fact]
    public async Task IsRevokedAsync_CheckThrows_FailsClosedWithoutCaching()
    {
        _check.Failure = new TimeoutException();

        bool revoked = await IsRevokedAsync(new TokenRevocationEnforcer(_check, _cache), Expiry.AddMinutes(-10));

        Assert.True(revoked);
        Assert.Empty(_cache.Writes);
        _logger.Records.ShouldHaveLoggedWithProperty(new(12103), "CheckAvailable", false);
    }

    [Fact]
    public async Task IsRevokedAsync_CheckCanceled_Propagates()
    {
        _check.Failure = new OperationCanceledException();

        await Assert.ThrowsAsync<OperationCanceledException>(() => IsRevokedAsync(new TokenRevocationEnforcer(_check, _cache), Expiry.AddMinutes(-10)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IsRevokedAsync_CacheReadThrows_CallsCheckAndLogs(bool checkAnswer)
    {
        _cache.ReadFailure = new InvalidOperationException("redis down");
        _check.IsRevoked = _ => checkAnswer;

        bool revoked = await IsRevokedAsync(new TokenRevocationEnforcer(_check, _cache), Expiry.AddMinutes(-10));

        Assert.Equal(checkAnswer, revoked);
        Assert.Single(_check.Requests);
        _logger.Records.ShouldHaveLogged(new(12106));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IsRevokedAsync_CacheWriteThrows_ReturnsCheckAnswerAndLogs(bool checkAnswer)
    {
        _cache.WriteFailure = new InvalidOperationException("redis down");
        _check.IsRevoked = _ => checkAnswer;

        bool revoked = await IsRevokedAsync(new TokenRevocationEnforcer(_check, _cache), Expiry.AddMinutes(-10));

        Assert.Equal(checkAnswer, revoked);
        _logger.Records.ShouldHaveLogged(new(12106));
    }

    [Fact]
    public async Task IsRevokedAsync_CacheReadCanceled_Propagates()
    {
        _cache.ReadFailure = new OperationCanceledException();

        await Assert.ThrowsAsync<OperationCanceledException>(() => IsRevokedAsync(new TokenRevocationEnforcer(_check, _cache), Expiry.AddMinutes(-10)));
        Assert.Empty(_check.Requests);
    }

    private Task<bool> IsRevokedAsync(TokenRevocationEnforcer enforcer, DateTime now) =>
        IsRevokedAsync(enforcer, new DateTimeOffset(now));

    private Task<bool> IsRevokedAsync(TokenRevocationEnforcer enforcer, DateTimeOffset now) =>
        enforcer.IsRevokedAsync(_token, AnonymousUserContext.Instance, _options, now, _logger, CancellationToken.None);
}
