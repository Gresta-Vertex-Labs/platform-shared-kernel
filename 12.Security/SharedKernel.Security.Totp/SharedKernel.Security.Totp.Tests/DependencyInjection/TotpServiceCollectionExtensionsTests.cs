using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Extensions;
using SharedKernel.Cryptography.Totp;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Security.Abstractions;
using SharedKernel.Security.Oidc.Extensions;
using SharedKernel.Security.Totp.Tests.TestDoubles;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Cryptography;
using SharedKernel.Testing.Security;
using Xunit;

namespace SharedKernel.Security.Totp.Tests.DependencyInjection;

public sealed class TotpServiceCollectionExtensionsTests
{
    private const string Session = "session-1";

    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 3, 1, 12, 0, 10, TimeSpan.Zero));
    private readonly InMemoryTotpStepUpStore _stepUps = new();

    [Fact]
    public async Task AddTotpStepUp_RegistersServicesWithExpectedLifetimes()
    {
        ServiceCollection services = CreateServices();

        services.AddSharedKernelCryptography(EmptyConfiguration()).AddTotpStepUp<InMemoryTotpStepUpStore, InMemoryRecoveryCodeStore>();

        Assert.Equal(ServiceLifetime.Scoped, Lifetime<TotpEnrollmentService>(services));
        Assert.Equal(ServiceLifetime.Scoped, Lifetime<TotpChallengeService>(services));
        Assert.Equal(ServiceLifetime.Scoped, Lifetime<ITotpStepUpStore>(services));
        Assert.Equal(ServiceLifetime.Scoped, Lifetime<IRecoveryCodeStore>(services));
        Assert.Equal(ServiceLifetime.Scoped, Lifetime<IClaimsTransformation>(services));
        Assert.Equal(ServiceLifetime.Singleton, Lifetime<ITotpVerifier>(services));
        await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<TotpEnrollmentService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<TotpChallengeService>());
        Assert.IsType<TotpVerifier>(scope.ServiceProvider.GetRequiredService<ITotpVerifier>());
        Assert.IsType<InMemoryTotpStepUpStore>(scope.ServiceProvider.GetRequiredService<ITotpStepUpStore>());
        Assert.IsType<InMemoryRecoveryCodeStore>(scope.ServiceProvider.GetRequiredService<IRecoveryCodeStore>());
        Assert.IsType<TotpStepUpClaimsTransformation>(scope.ServiceProvider.GetRequiredService<IClaimsTransformation>());
        Assert.Same(_clock, scope.ServiceProvider.GetRequiredService<IClock>());
    }

    [Fact]
    public void AddTotpStepUp_WithoutClock_RegistersSystemClock()
    {
        var services = new ServiceCollection();

        services.AddSharedKernelCryptography(EmptyConfiguration()).AddTotpStepUp<InMemoryTotpStepUpStore, InMemoryRecoveryCodeStore>();

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IClock));
    }

    [Fact]
    public async Task AddTotpStepUp_StoresRegisteredEarlier_AreKept()
    {
        ServiceCollection services = CreateServices();
        services.AddSingleton<ITotpStepUpStore>(_stepUps);

        services.AddSharedKernelCryptography(EmptyConfiguration()).AddTotpStepUp<InMemoryTotpStepUpStore, InMemoryRecoveryCodeStore>();

        await using ServiceProvider provider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        Assert.Same(_stepUps, scope.ServiceProvider.GetRequiredService<ITotpStepUpStore>());
    }

    [Fact]
    public async Task AddTotpStepUp_ResolvedChallengeService_RecordsStepUpThatResolvedTransformationApplies()
    {
        ServiceCollection services = CreateServices();
        services.AddSingleton<ITotpStepUpStore>(_stepUps);
        services.AddSharedKernelCryptography(EmptyConfiguration()).AddTotpStepUp<InMemoryTotpStepUpStore, InMemoryRecoveryCodeStore>();
        await using ServiceProvider provider = services.BuildServiceProvider();
        byte[] secret = TotpTestHarness.NewSecret();
        string code = new TotpGenerator(_clock).GenerateCode(secret);

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            TotpChallengeResult result = await scope.ServiceProvider.GetRequiredService<TotpChallengeService>()
                .VerifyCodeAsync(TotpTestHarness.User(Session), secret, code);
            Assert.Equal(TotpChallengeResult.Verified, result);
        }

        await using AsyncServiceScope next = provider.CreateAsyncScope();
        ClaimsPrincipal principal = await next.ServiceProvider.GetRequiredService<IClaimsTransformation>()
            .TransformAsync(new SecurityTestContextBuilder().WithSessionId(Session).Build());
        Assert.True(principal.HasClaim("amr", "otp"));
    }

    [Fact]
    public async Task AddTotpStepUp_ThrottleRegistered_IsUsedByResolvedServices()
    {
        ServiceCollection services = CreateServices();
        var throttle = new RecordingAttemptThrottle { Throttled = true };
        services.AddSingleton<ITotpAttemptThrottle>(throttle);
        services.AddSharedKernelCryptography(EmptyConfiguration()).AddTotpStepUp<InMemoryTotpStepUpStore, InMemoryRecoveryCodeStore>();
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        byte[] secret = TotpTestHarness.NewSecret();
        string code = new TotpGenerator(_clock).GenerateCode(secret);

        TotpChallengeResult challenge = await scope.ServiceProvider.GetRequiredService<TotpChallengeService>()
            .VerifyCodeAsync(TotpTestHarness.User(), secret, code);
        TotpChallengeResult recovery = await scope.ServiceProvider.GetRequiredService<TotpChallengeService>()
            .RedeemRecoveryCodeAsync(TotpTestHarness.User(), "AAAAA-BBBBB");
        TotpChallengeResult confirm = await scope.ServiceProvider.GetRequiredService<TotpEnrollmentService>()
            .ConfirmAsync(TotpTestHarness.User(), secret, code);

        Assert.Equal(TotpChallengeResult.Throttled, challenge);
        Assert.Equal(TotpChallengeResult.Throttled, recovery);
        Assert.Equal(TotpChallengeResult.Throttled, confirm);
        Assert.Equal(3, throttle.Checks.Count);
    }

    [Fact]
    public async Task AddTotpStepUp_ConfigureDelegate_IsAppliedToOptions()
    {
        ServiceCollection services = CreateServices();

        services.AddSharedKernelCryptography(EmptyConfiguration()).AddTotpStepUp<InMemoryTotpStepUpStore, InMemoryRecoveryCodeStore>(options =>
        {
            options.FreshnessWindow = TimeSpan.FromMinutes(3);
            options.AuthenticationMethod = "totp";
        });

        await using ServiceProvider provider = services.BuildServiceProvider();
        TotpStepUpOptions resolved = provider.GetRequiredService<IOptions<TotpStepUpOptions>>().Value;
        Assert.Equal(TimeSpan.FromMinutes(3), resolved.FreshnessWindow);
        Assert.Equal("totp", resolved.AuthenticationMethod);
        Assert.Equal("amr", resolved.AuthenticationMethodClaimType);
    }

    public static TheoryData<string> InvalidOptions => ["window-59s", "window-24h1t", "window-zero", "window-negative", "claim-type-empty", "claim-type-whitespace", "method-empty", "method-null"];

    [Theory]
    [MemberData(nameof(InvalidOptions))]
    public async Task AddTotpStepUp_InvalidOptions_FailsHostStartup(string invalid)
    {
        using IHost host = BuildHost(options =>
        {
            switch (invalid)
            {
                case "window-59s": options.FreshnessWindow = TimeSpan.FromSeconds(59); break;
                case "window-24h1t": options.FreshnessWindow = TimeSpan.FromHours(24) + TimeSpan.FromTicks(1); break;
                case "window-zero": options.FreshnessWindow = TimeSpan.Zero; break;
                case "window-negative": options.FreshnessWindow = TimeSpan.FromMinutes(-15); break;
                case "claim-type-empty": options.AuthenticationMethodClaimType = string.Empty; break;
                case "claim-type-whitespace": options.AuthenticationMethodClaimType = "  "; break;
                case "method-empty": options.AuthenticationMethod = string.Empty; break;
                default: options.AuthenticationMethod = null!; break;
            }
        });

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(1440)]
    public async Task AddTotpStepUp_WindowWithinRange_HostStarts(int minutes)
    {
        using IHost host = BuildHost(options => options.FreshnessWindow = TimeSpan.FromMinutes(minutes));

        await host.StartAsync();
        await host.StopAsync();
    }

    [Fact]
    public void AddTotpStepUp_CalledTwice_RegistersTransformationOnce()
    {
        ServiceCollection services = CreateServices();
        ICryptographyBuilder cryptography = services.AddSharedKernelCryptography(EmptyConfiguration());

        cryptography.AddTotpStepUp<InMemoryTotpStepUpStore, InMemoryRecoveryCodeStore>();
        cryptography.AddTotpStepUp<InMemoryTotpStepUpStore, InMemoryRecoveryCodeStore>();

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IClaimsTransformation));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(TotpChallengeService));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(TotpEnrollmentService));
    }

    [Fact]
    public async Task AddTotpStepUp_CalledTwice_ResolvedTransformationDoesNotWrapItself()
    {
        ServiceCollection services = CreateServices();
        ICryptographyBuilder cryptography = services.AddSharedKernelCryptography(EmptyConfiguration());
        cryptography.AddTotpStepUp<InMemoryTotpStepUpStore, InMemoryRecoveryCodeStore>();
        cryptography.AddTotpStepUp<InMemoryTotpStepUpStore, InMemoryRecoveryCodeStore>();

        await using ServiceProvider provider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();

        Assert.Null(InnerOf(scope.ServiceProvider.GetRequiredService<IClaimsTransformation>()));
    }

    [Fact]
    public async Task AddTotpStepUp_TransformationRegisteredEarlierByType_IsWrappedAndRunsFirst()
    {
        ServiceCollection services = CreateServices();
        services.AddScoped<IClaimsTransformation, MarkerClaimsTransformation>();

        services.AddSharedKernelCryptography(EmptyConfiguration()).AddTotpStepUp<InMemoryTotpStepUpStore, InMemoryRecoveryCodeStore>();

        ClaimsPrincipal result = await TransformSteppedUpPrincipalAsync(services);
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IClaimsTransformation) && !descriptor.IsKeyedService);
        Assert.True(result.HasClaim(MarkerClaimsTransformation.ClaimType, "type"));
        Assert.True(result.HasClaim("amr", "otp"));
    }

    [Fact]
    public async Task AddTotpStepUp_TransformationRegisteredEarlierByFactory_IsWrappedAndRunsFirst()
    {
        ServiceCollection services = CreateServices();
        services.AddScoped<IClaimsTransformation>(provider =>
            new MarkerClaimsTransformation(provider.GetRequiredService<TransformationLog>()) { Tag = "factory" });

        services.AddSharedKernelCryptography(EmptyConfiguration()).AddTotpStepUp<InMemoryTotpStepUpStore, InMemoryRecoveryCodeStore>();

        ClaimsPrincipal result = await TransformSteppedUpPrincipalAsync(services);
        Assert.True(result.HasClaim(MarkerClaimsTransformation.ClaimType, "factory"));
        Assert.True(result.HasClaim("amr", "otp"));
    }

    [Fact]
    public async Task AddTotpStepUp_TransformationRegisteredEarlierByInstance_IsWrappedAndRunsFirst()
    {
        ServiceCollection services = CreateServices();
        var log = new TransformationLog();
        services.AddSingleton<IClaimsTransformation>(new MarkerClaimsTransformation(log) { Tag = "instance" });

        services.AddSharedKernelCryptography(EmptyConfiguration()).AddTotpStepUp<InMemoryTotpStepUpStore, InMemoryRecoveryCodeStore>();

        ClaimsPrincipal result = await TransformSteppedUpPrincipalAsync(services);
        Assert.True(result.HasClaim(MarkerClaimsTransformation.ClaimType, "instance"));
        Assert.True(result.HasClaim("amr", "otp"));
        Assert.Equal("instance", Assert.Single(log.Entries));
    }

    [Fact]
    public async Task AddTotpStepUp_OnlyLastEarlierTransformationIsWrapped()
    {
        ServiceCollection services = CreateServices();
        services.AddScoped<IClaimsTransformation>(provider => new MarkerClaimsTransformation(provider.GetRequiredService<TransformationLog>()) { Tag = "first" });
        services.AddScoped<IClaimsTransformation>(provider => new MarkerClaimsTransformation(provider.GetRequiredService<TransformationLog>()) { Tag = "last" });

        services.AddSharedKernelCryptography(EmptyConfiguration()).AddTotpStepUp<InMemoryTotpStepUpStore, InMemoryRecoveryCodeStore>();

        ClaimsPrincipal result = await TransformSteppedUpPrincipalAsync(services);
        Assert.True(result.HasClaim(MarkerClaimsTransformation.ClaimType, "last"));
        Assert.False(result.HasClaim(MarkerClaimsTransformation.ClaimType, "first"));
    }

    [Fact]
    public async Task AddTotpStepUp_AfterAddAuthentication_ReplacesNoopWithoutWrappingIt()
    {
        ServiceCollection services = CreateServices();
        services.AddAuthentication();
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IClaimsTransformation) && descriptor.ImplementationType?.Name == "NoopClaimsTransformation");

        services.AddSharedKernelCryptography(EmptyConfiguration()).AddTotpStepUp<InMemoryTotpStepUpStore, InMemoryRecoveryCodeStore>();

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IClaimsTransformation));
        services.AddSingleton<ITotpStepUpStore>(_stepUps);
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IClaimsTransformation transformation = scope.ServiceProvider.GetRequiredService<IClaimsTransformation>();
        Assert.Null(InnerOf(transformation));
        ClaimsPrincipal result = await TransformSteppedUpPrincipalAsync(provider);
        Assert.True(result.HasClaim("amr", "otp"));
    }

    [Fact]
    public async Task AddTotpStepUp_BeforeAddAuthentication_IsNotReplacedByNoop()
    {
        ServiceCollection services = CreateServices();

        services.AddSharedKernelCryptography(EmptyConfiguration()).AddTotpStepUp<InMemoryTotpStepUpStore, InMemoryRecoveryCodeStore>();
        services.AddAuthentication();

        await using ServiceProvider provider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        Assert.IsType<TotpStepUpClaimsTransformation>(scope.ServiceProvider.GetRequiredService<IClaimsTransformation>());
    }

    [Fact]
    public async Task AddTotpStepUp_AfterAddOidcAuthentication_UsesOidcMapperForStepUp()
    {
        ServiceCollection services = CreateServices(registerTestMapper: false);
        services.AddSingleton<ITotpStepUpStore>(_stepUps);
        services.AddOidcAuthentication(OidcConfiguration());
        services.AddSharedKernelCryptography(EmptyConfiguration()).AddTotpStepUp<InMemoryTotpStepUpStore, InMemoryRecoveryCodeStore>();
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        await _stepUps.RecordAsync(FakeUserContext.DefaultSubjectId, Session, _clock.UtcNow, _clock.UtcNow.AddMinutes(15), CancellationToken.None);

        ClaimsPrincipal result = await scope.ServiceProvider.GetRequiredService<IClaimsTransformation>()
            .TransformAsync(new SecurityTestContextBuilder().WithSessionId(Session).WithAuthenticationMethods("pwd").Build());

        IUserContext user = UserContextResolver.Resolve(result, scope.ServiceProvider.GetServices<IUserContextMapper>());
        Assert.Equal(IdentityKind.User, user.IdentityKind);
        Assert.True(user.WasAuthenticatedWith("otp"));
        Assert.True(user.WasAuthenticatedWith("pwd"));
    }

    [Fact]
    public async Task AddTotpStepUp_OidcTokenWithoutSid_StepsUpTokenIdSession()
    {
        ServiceCollection services = CreateServices(registerTestMapper: false);
        services.AddSingleton<ITotpStepUpStore>(_stepUps);
        services.AddOidcAuthentication(OidcConfiguration());
        services.AddSharedKernelCryptography(EmptyConfiguration()).AddTotpStepUp<InMemoryTotpStepUpStore, InMemoryRecoveryCodeStore>();
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        await _stepUps.RecordAsync(FakeUserContext.DefaultSubjectId, "token-1", _clock.UtcNow, _clock.UtcNow.AddMinutes(15), CancellationToken.None);
        IClaimsTransformation transformation = scope.ServiceProvider.GetRequiredService<IClaimsTransformation>();

        ClaimsPrincipal sameToken = await transformation.TransformAsync(new SecurityTestContextBuilder().WithClaim("jti", "token-1").Build());
        ClaimsPrincipal refreshedToken = await transformation.TransformAsync(new SecurityTestContextBuilder().WithClaim("jti", "token-2").Build());

        Assert.True(sameToken.HasClaim("amr", "otp"));
        Assert.False(refreshedToken.HasClaim("amr", "otp"));
    }

    [Fact]
    public async Task AddTotpStepUp_KeyedTransformationRegisteredLast_KeepsKeyedAndWrapsUnkeyed()
    {
        ServiceCollection services = CreateServices();
        services.AddScoped<IClaimsTransformation>(provider => new MarkerClaimsTransformation(provider.GetRequiredService<TransformationLog>()) { Tag = "unkeyed" });
        services.AddKeyedScoped<IClaimsTransformation>("other", (provider, _) => new MarkerClaimsTransformation(provider.GetRequiredService<TransformationLog>()) { Tag = "keyed" });

        services.AddSharedKernelCryptography(EmptyConfiguration()).AddTotpStepUp<InMemoryTotpStepUpStore, InMemoryRecoveryCodeStore>();
        services.AddSingleton<ITotpStepUpStore>(_stepUps);

        await using ServiceProvider provider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        Assert.NotNull(scope.ServiceProvider.GetKeyedService<IClaimsTransformation>("other"));
        ClaimsPrincipal result = await TransformSteppedUpPrincipalAsync(provider);
        Assert.True(result.HasClaim(MarkerClaimsTransformation.ClaimType, "unkeyed"));
        Assert.True(result.HasClaim("amr", "otp"));
    }

    [Fact]
    public async Task AddTotpStepUp_SingletonTransformationRegisteredEarlierByType_KeepsSingleInstance()
    {
        ServiceCollection services = CreateServices();
        services.AddSingleton<IClaimsTransformation, CountingClaimsTransformation>();
        services.AddSharedKernelCryptography(EmptyConfiguration()).AddTotpStepUp<InMemoryTotpStepUpStore, InMemoryRecoveryCodeStore>();
        CountingClaimsTransformation.Reset();

        await using (ServiceProvider provider = services.BuildServiceProvider())
        {
            for (int i = 0; i < 3; i++)
            {
                await using AsyncServiceScope scope = provider.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IClaimsTransformation>().TransformAsync(new ClaimsPrincipal());
            }
        }

        Assert.Equal(1, CountingClaimsTransformation.Instances);
        Assert.Equal(1, CountingClaimsTransformation.Disposals);
    }

    [Fact]
    public void AddTotpStepUp_NullBuilder_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => TotpServiceCollectionExtensions.AddTotpStepUp<InMemoryTotpStepUpStore, InMemoryRecoveryCodeStore>(null!));
    }

    private ServiceCollection CreateServices(bool registerTestMapper = true)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IClock>(_clock);
        services.AddSingleton<ITotpReplayGuard>(new FakeTotpReplayGuard(_clock));
        services.AddSingleton<TransformationLog>();
        if (registerTestMapper)
        {
            services.AddSingleton<IUserContextMapper>(new TestUserContextMapper());
        }

        return services;
    }

    private IHost BuildHost(Action<TotpStepUpOptions> configure) =>
        new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IClock>(_clock);
                services.AddSingleton<ITotpReplayGuard>(new FakeTotpReplayGuard(_clock));
                services.AddSharedKernelCryptography(EmptyConfiguration()).AddTotpStepUp<InMemoryTotpStepUpStore, InMemoryRecoveryCodeStore>(configure);
            })
            .Build();

    private async Task<ClaimsPrincipal> TransformSteppedUpPrincipalAsync(IServiceCollection services)
    {
        services.AddSingleton<ITotpStepUpStore>(_stepUps);
        await using ServiceProvider provider = services.BuildServiceProvider();
        return await TransformSteppedUpPrincipalAsync(provider);
    }

    private async Task<ClaimsPrincipal> TransformSteppedUpPrincipalAsync(IServiceProvider provider)
    {
        await _stepUps.RecordAsync(FakeUserContext.DefaultSubjectId, Session, _clock.UtcNow, _clock.UtcNow.AddMinutes(15), CancellationToken.None);
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IClaimsTransformation transformation = scope.ServiceProvider.GetRequiredService<IClaimsTransformation>();
        Assert.IsType<TotpStepUpClaimsTransformation>(transformation);
        return await transformation.TransformAsync(new SecurityTestContextBuilder().WithSessionId(Session).Build());
    }

    private static IClaimsTransformation? InnerOf(IClaimsTransformation transformation) =>
        (IClaimsTransformation?)typeof(TotpStepUpClaimsTransformation)
            .GetField("_inner", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(Assert.IsType<TotpStepUpClaimsTransformation>(transformation));

    private static ServiceLifetime Lifetime<TService>(IServiceCollection services) =>
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(TService)).Lifetime;

    private static IConfiguration EmptyConfiguration() => new ConfigurationBuilder().Build();

    private static IConfiguration OidcConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SharedKernel:Security:Oidc:Authority"] = "https://idp.example.test",
                ["SharedKernel:Security:Oidc:Audiences:0"] = "api",
            })
            .Build();

    private sealed class CountingClaimsTransformation : IClaimsTransformation, IDisposable
    {
        private static int s_instances;
        private static int s_disposals;

        public CountingClaimsTransformation() => Interlocked.Increment(ref s_instances);

        public static int Instances => s_instances;

        public static int Disposals => s_disposals;

        public static void Reset()
        {
            s_instances = 0;
            s_disposals = 0;
        }

        public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal) => Task.FromResult(principal);

        public void Dispose() => Interlocked.Increment(ref s_disposals);
    }
}
