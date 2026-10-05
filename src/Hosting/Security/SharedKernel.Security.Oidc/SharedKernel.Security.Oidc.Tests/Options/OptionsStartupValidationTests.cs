using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Security.Oidc.Tests.Infrastructure;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Logging;
using Xunit;

namespace SharedKernel.Security.Oidc.Tests.Options;

public sealed class OptionsStartupValidationTests
{
    [Theory]
    [InlineData("SharedKernel:Security:Oidc:Authority", "http://issuer.example.test", "Authority must use https")]
    [InlineData("SharedKernel:Security:Oidc:ValidAlgorithms:0", "HS256", "ValidAlgorithms contains 'HS256'")]
    [InlineData("SharedKernel:Security:Oidc:Dpop:ValidAlgorithms:0", "none", "Dpop.ValidAlgorithms contains 'none'")]
    [InlineData("SharedKernel:Security:Oidc:ClockSkew", "00:10:00", "ClockSkew must be between")]
    [InlineData("SharedKernel:Security:Oidc:Revocation:NotRevokedCacheDuration", "01:00:00", "Revocation.NotRevokedCacheDuration must be between")]
    public async Task StartAsync_InvalidConfiguration_Throws(string key, string value, string expectedFailure)
    {
        var options = new OidcTestHostOptions();
        options.Settings[key] = value;
        using IHost host = OidcTestHost.Build(options, new FakeClock(), new InMemoryLoggerFactory());

        OptionsValidationException exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains(exception.Failures, failure => failure.Contains(expectedFailure, StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartAsync_MissingSection_Throws()
    {
        var options = new OidcTestHostOptions();
        options.Settings.Clear();
        using IHost host = OidcTestHost.Build(options, new FakeClock(), new InMemoryLoggerFactory());

        OptionsValidationException exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains("Authority must be an absolute URL.", exception.Failures);
        Assert.Contains("Audiences must contain at least one value and no empty values.", exception.Failures);
    }

    [Fact]
    public async Task StartAsync_ValidConfiguration_Starts()
    {
        var options = new OidcTestHostOptions();
        options.Settings["SharedKernel:Security:Oidc:ValidAlgorithms:0"] = "PS256";
        options.Settings["SharedKernel:Security:Oidc:Dpop:Mode"] = "Required";
        options.Oidc = oidc => oidc.AddDpop<SharedKernel.Testing.Security.InMemoryDpopReplayCache>();
        using IHost host = OidcTestHost.Build(options, new FakeClock(), new InMemoryLoggerFactory());

        await host.StartAsync();
        await host.StopAsync();
    }
}
