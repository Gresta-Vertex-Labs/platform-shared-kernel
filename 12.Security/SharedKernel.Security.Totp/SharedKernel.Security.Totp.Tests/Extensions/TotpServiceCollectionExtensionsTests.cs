using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Cryptography.Random;
using SharedKernel.Cryptography.Totp;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Security.Totp.Challenge;
using SharedKernel.Security.Totp.Enrollment;
using SharedKernel.Security.Totp.Extensions;
using SharedKernel.Security.Totp.StepUp;
using SharedKernel.Security.Totp.Tests.Challenge;
using SharedKernel.Testing.Clocks;
using Xunit;

namespace SharedKernel.Security.Totp.Tests.Extensions;

public sealed class TotpServiceCollectionExtensionsTests
{
    [Fact]
    public void AddTotpStepUp_NullServices_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            TotpServiceCollectionExtensions.AddTotpStepUp<DummyChallengeStore>(null!));
    }

    [Fact]
    public void AddTotpStepUp_RegistersITotpChallengeStore_AsScoped()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTotpStepUp<DummyChallengeStore>();

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ITotpChallengeStore));

        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Scoped, descriptor!.Lifetime);
        Assert.Equal(typeof(DummyChallengeStore), descriptor.ImplementationType);
    }

    [Fact]
    public void AddTotpStepUp_RegistersIClaimsTransformation_AsTotpStepUpClaimsTransformation()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTotpStepUp<DummyChallengeStore>();

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IClaimsTransformation));

        Assert.NotNull(descriptor);
        Assert.Equal(typeof(TotpStepUpClaimsTransformation), descriptor!.ImplementationType);
    }

    [Fact]
    public void AddTotpStepUp_RegistersTotpChallengeService_AndItResolves()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        RegisterFakeTotpPrimitives(services);
        services.AddTotpStepUp<DummyChallengeStore>();
        var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<TotpChallengeService>();

        Assert.NotNull(service);
    }

    [Fact]
    public void AddTotpStepUp_RegistersTotpEnrollmentService_AndItResolves()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ISecureRandomGenerator, CryptoRandomGenerator>();
        services.AddTotpStepUp<DummyChallengeStore>();
        var provider = services.BuildServiceProvider();

        var service = provider.GetRequiredService<TotpEnrollmentService>();

        Assert.NotNull(service);
    }

    [Fact]
    public void AddTotpStepUp_ConfigureOptionsDelegate_IsApplied()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTotpStepUp<DummyChallengeStore>(options =>
        {
            options.AmrValue = "custom-otp";
            options.ChallengeFreshnessWindow = TimeSpan.FromMinutes(5);
        });
        var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<TotpStepUpOptions>();

        Assert.Equal("custom-otp", options.AmrValue);
        Assert.Equal(TimeSpan.FromMinutes(5), options.ChallengeFreshnessWindow);
    }

    private static void RegisterFakeTotpPrimitives(IServiceCollection services)
    {
        // Mirrors 01.Core's AddSharedKernelCryptography's own TotpVerifier composition
        // (IClock -> IHotpGenerator -> ITotpGenerator -> TotpVerifier), constructed directly here
        // rather than pulling in a real IClock registration, since this test only proves DI shape.
        services.AddSingleton<IClock>(new FakeClock());
        services.AddSingleton<IHotpGenerator, HotpGenerator>();
        services.AddSingleton<ITotpGenerator, TotpGenerator>();
        services.AddSingleton<ITotpReplayGuard, FakeTotpReplayGuard>();
        services.AddSingleton<TotpVerifier>();
    }

    private sealed class DummyChallengeStore : ITotpChallengeStore
    {
        public Task RecordSuccessfulChallengeAsync(string identityKey, DateTimeOffset verifiedAt, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<DateTimeOffset?> TryGetLastSuccessfulChallengeAsync(string identityKey, CancellationToken ct = default) =>
            Task.FromResult((DateTimeOffset?)null);
    }
}
