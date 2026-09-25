using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Security.Abstractions;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Security;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Security;

/// <summary>
/// P-562 X1: with <see cref="FakeUserContext.WithAuthenticationMethodTime"/>, a consumer test drives a step-up with a
/// maximum age — <c>[RequireAuthenticationMethod("otp", MaxAgeSeconds = 300)]</c> — both fresh and expired. Evaluated by
/// <c>SharedKernel.Presentation.WebApi</c>'s own authorization handler (reached through this project's test-only
/// reference to the gRPC package), the consumer these helpers exist for.
/// </summary>
public sealed class AuthenticationMethodMaxAgeTests
{
    private const string TestScheme = "Test";
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(299, true)]
    [InlineData(300, true)]
    [InlineData(301, false)]
    public async Task FakeUserContext_StepUpWithinTheMaximumAge_IsAccepted_AndExpiresAfterIt(int ageSeconds, bool accepted)
    {
        var user = new FakeUserContext { AuthenticationMethods = ["pwd", "otp"], AuthTime = Now.AddHours(-1) }
            .WithAuthenticationMethodTime("otp", Now.AddSeconds(-ageSeconds));

        Assert.Equal(accepted, await AuthorizeAsync(user));
    }

    [Fact]
    public async Task FakeUserContext_MethodWithoutItsOwnTime_IsDatedByAuthTime()
    {
        var fresh = new FakeUserContext { AuthenticationMethods = ["otp"], AuthTime = Now.AddMinutes(-1) };
        var expired = new FakeUserContext { AuthenticationMethods = ["otp"], AuthTime = Now.AddMinutes(-10) };

        Assert.True(await AuthorizeAsync(fresh));
        Assert.False(await AuthorizeAsync(expired));
    }

    [Fact]
    public async Task BuiltUserContext_StepUpOfAGivenAge_IsFreshOrExpired()
    {
        var builder = new SecurityTestContextBuilder().WithAuthenticationMethods("pwd", "otp").WithAuthTime(Now.AddHours(-1));

        Assert.True(await AuthorizeAsync(builder.WithAuthenticationMethodTime("otp", Now.AddMinutes(-4)).BuildUserContext()));
        Assert.False(await AuthorizeAsync(builder.WithAuthenticationMethodTime("otp", Now.AddMinutes(-6)).BuildUserContext()));
    }

    // Authorizes like an endpoint carrying the attribute: the platform policy for its name, the caller mapped from the
    // principal by the registered IUserContextMapper, the time from IClock.
    private static async Task<bool> AuthorizeAsync(IUserContext user)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelAuthorization();
        services.AddSingleton<IClock>(new FakeClock(Now));
        services.AddSingleton<IUserContextMapper>(new FixedUserContextMapper(user));
        await using var provider = services.BuildServiceProvider();

        var policy = await AuthorizationPolicy.CombineAsync(
            provider.GetRequiredService<IAuthorizationPolicyProvider>(),
            [new RequireAuthenticationMethodAttribute("otp") { MaxAgeSeconds = 300 }]);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(SecurityClaimTypes.Subject, "user-1")], TestScheme));

        var result = await provider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(principal, resource: null, policy!);
        return result.Succeeded;
    }

    /// <summary>Maps every identity of the test scheme to one prepared context, as a consumer's test host can.</summary>
    private sealed class FixedUserContextMapper(IUserContext user) : IUserContextMapper
    {
        public string AuthenticationType => TestScheme;

        public IUserContext Map(ClaimsIdentity identity) => user;
    }
}
