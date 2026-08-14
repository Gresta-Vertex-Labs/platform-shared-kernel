using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.ServiceDefaults.Security;

namespace SharedKernel.ServiceDefaults.Tests.Security;

/// <summary>
/// Covers T-44's acceptance criterion that <see cref="MtlsForwardedHeaderOptions"/> with an unconfigured
/// <see cref="MtlsForwardedHeaderOptions.HeaderName"/> fails fast at startup via options validation —
/// asserted directly by starting a real <see cref="IHost"/>, not merely documented in XML comments.
/// </summary>
public sealed class MtlsForwardedHeaderExtensionsTests
{
    [Fact]
    public void AddMtlsForwardedHeaderCertificate_NullBuilder_ThrowsArgumentNullException()
    {
        IHostApplicationBuilder builder = null!;

        var act = () => builder.AddMtlsForwardedHeaderCertificate(o => o.HeaderName = "ssl-client-cert");

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddMtlsForwardedHeaderCertificate_NullConfigure_ThrowsArgumentNullException()
    {
        var builder = Host.CreateApplicationBuilder();

        var act = () => builder.AddMtlsForwardedHeaderCertificate(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddMtlsForwardedHeaderCertificate_ReturnsSameBuilderInstance()
    {
        var builder = Host.CreateApplicationBuilder();

        var result = builder.AddMtlsForwardedHeaderCertificate(o => o.HeaderName = "ssl-client-cert");

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public async Task AddMtlsForwardedHeaderCertificate_ConfiguredHeaderName_StartsUpWithoutThrowing()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddMtlsForwardedHeaderCertificate(o => o.HeaderName = "ssl-client-cert");
        using var host = builder.Build();

        await host.StartAsync();
        try
        {
            host.Services.GetRequiredService<IOptions<MtlsForwardedHeaderOptions>>().Value.HeaderName
                .Should().Be("ssl-client-cert");
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task AddMtlsForwardedHeaderCertificate_UnconfiguredHeaderName_FailsFastAtStartup()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddMtlsForwardedHeaderCertificate(_ => { }); // HeaderName left at its string.Empty default
        using var host = builder.Build();

        var act = async () => await host.StartAsync();

        await act.Should().ThrowAsync<OptionsValidationException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AddMtlsForwardedHeaderCertificate_NullOrWhitespaceHeaderName_FailsFastAtStartup(string headerName)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddMtlsForwardedHeaderCertificate(o => o.HeaderName = headerName);
        using var host = builder.Build();

        var act = async () => await host.StartAsync();

        await act.Should().ThrowAsync<OptionsValidationException>();
    }

    [Fact]
    public void AddMtlsForwardedHeaderCertificate_RegistersAnOptionsValidator()
    {
        // Distinguishes "this method was called" from the no-op baseline (MtlsNoOpRegressionTests) at the
        // DI-registration level: a registered IValidateOptions<MtlsForwardedHeaderOptions> is what makes
        // the fail-fast-at-startup behavior possible in the first place.
        var builder = Host.CreateApplicationBuilder();
        builder.AddMtlsForwardedHeaderCertificate(o => o.HeaderName = "ssl-client-cert");
        using var host = builder.Build();

        host.Services.GetServices<IValidateOptions<MtlsForwardedHeaderOptions>>().Should().NotBeEmpty();
    }
}
