using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Cryptography.Extensions;
using SharedKernel.Cryptography.Totp;
using SharedKernel.Security.Totp.Challenge;
using SharedKernel.Security.Totp.Enrollment;
using SharedKernel.Security.Totp.Extensions;
using SharedKernel.Security.Totp.StepUp;
using SharedKernel.Testing.Cryptography;
using Xunit;

namespace SharedKernel.Security.Totp.Tests.Extensions;

public sealed class TotpServiceCollectionExtensionsTests
{
    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        return services;
    }

    private static ICryptographyBuilder AddCryptography(IServiceCollection services) =>
        services.AddSharedKernelCryptography(new ConfigurationBuilder().Build());

    [Fact]
    public void AddTotpStepUp_NullBuilder_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            TotpServiceCollectionExtensions.AddTotpStepUp<DummyChallengeStore>(null!));
    }

    [Fact]
    public void AddTotpStepUp_ReturnsSameBuilder_ForChaining()
    {
        var builder = AddCryptography(CreateServices());

        var result = builder.AddTotpStepUp<DummyChallengeStore>();

        Assert.Same(builder, result);
    }

    [Fact]
    public void AddTotpStepUp_RegistersITotpVerifier()
    {
        var services = CreateServices();
        AddCryptography(services).AddTotpStepUp<DummyChallengeStore>();

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(ITotpVerifier));

        Assert.Equal(typeof(TotpVerifier), descriptor.ImplementationType);
    }

    [Fact]
    public void AddTotpStepUp_RegistersITotpChallengeStore_AsScoped()
    {
        var services = CreateServices();
        AddCryptography(services).AddTotpStepUp<DummyChallengeStore>();

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ITotpChallengeStore));

        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Scoped, descriptor!.Lifetime);
        Assert.Equal(typeof(DummyChallengeStore), descriptor.ImplementationType);
    }

    [Fact]
    public void AddTotpStepUp_RegistersIClaimsTransformation_AsTotpStepUpClaimsTransformation()
    {
        var services = CreateServices();
        AddCryptography(services).AddTotpStepUp<DummyChallengeStore>();

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IClaimsTransformation));

        Assert.NotNull(descriptor);
        Assert.Equal(typeof(TotpStepUpClaimsTransformation), descriptor!.ImplementationType);
    }

    [Fact]
    public void AddTotpStepUp_WithReplayGuard_TotpChallengeServiceResolves()
    {
        var services = CreateServices();
        services.AddSingleton<ITotpReplayGuard, FakeTotpReplayGuard>();
        AddCryptography(services).AddTotpStepUp<DummyChallengeStore>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<TotpChallengeService>();

        Assert.NotNull(service);
    }

    [Fact]
    public void AddTotpStepUp_WithoutReplayGuard_TotpChallengeServiceFailsToResolve()
    {
        var services = CreateServices();
        AddCryptography(services).AddTotpStepUp<DummyChallengeStore>();
        using var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();

        Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<TotpChallengeService>());
    }

    [Fact]
    public void AddTotpStepUp_TotpEnrollmentServiceResolves_WithoutAReplayGuard()
    {
        var services = CreateServices();
        AddCryptography(services).AddTotpStepUp<DummyChallengeStore>();
        using var provider = services.BuildServiceProvider();

        var service = provider.GetRequiredService<TotpEnrollmentService>();

        Assert.NotNull(service);
    }

    [Fact]
    public void AddTotpStepUp_ConfigureOptionsDelegate_IsApplied()
    {
        var services = CreateServices();
        AddCryptography(services).AddTotpStepUp<DummyChallengeStore>(options =>
        {
            options.AmrValue = "custom-otp";
            options.ChallengeFreshnessWindow = TimeSpan.FromMinutes(5);
        });
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<TotpStepUpOptions>();

        Assert.Equal("custom-otp", options.AmrValue);
        Assert.Equal(TimeSpan.FromMinutes(5), options.ChallengeFreshnessWindow);
    }

    private sealed class DummyChallengeStore : ITotpChallengeStore
    {
        public Task RecordSuccessfulChallengeAsync(string identityKey, DateTimeOffset verifiedAt, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<DateTimeOffset?> TryGetLastSuccessfulChallengeAsync(string identityKey, CancellationToken ct = default) =>
            Task.FromResult((DateTimeOffset?)null);
    }
}
