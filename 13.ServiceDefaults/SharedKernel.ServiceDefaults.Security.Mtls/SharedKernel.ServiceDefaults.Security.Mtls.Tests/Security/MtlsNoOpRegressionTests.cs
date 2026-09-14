using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.ServiceDefaults.Security;

namespace SharedKernel.ServiceDefaults.Security.Mtls.Tests.Security;

/// <summary>
/// Covers T-44's "no behavior change for non-adopting consumers" gating acceptance criterion: a host
/// that calls neither <see cref="MtlsClientCertificateExtensions.AddMtlsClientCertificate"/> nor
/// <see cref="MtlsForwardedHeaderExtensions.AddMtlsForwardedHeaderCertificate"/> must be byte-identical
/// in behavior to a host built before WO-058 ever existed — proven directly against the real DI
/// container and the real <see cref="KestrelServerOptions"/> instance a built host produces, not
/// inferred from the absence of a test for the opted-in path.
/// </summary>
public sealed class MtlsNoOpRegressionTests
{
    private static Action<HttpsConnectionAdapterOptions> GetHttpsDefaultsDelegate(KestrelServerOptions options)
    {
        var property = typeof(KestrelServerOptions).GetProperty("HttpsDefaults", BindingFlags.NonPublic | BindingFlags.Instance);
        return (Action<HttpsConnectionAdapterOptions>)property!.GetValue(options)!;
    }

    [Fact]
    public void NeitherMtlsExtensionCalled_KestrelClientCertificateModeAndValidationDelegateAreUntouched()
    {
        var builder = Host.CreateApplicationBuilder();
        using var host = builder.Build();

        var kestrelOptions = host.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value;
        var httpsDefaults = GetHttpsDefaultsDelegate(kestrelOptions);

        var httpsOptions = new HttpsConnectionAdapterOptions();
        httpsDefaults(httpsOptions);

        // Kestrel's own out-of-the-box defaults, confirmed by decompiling HttpsConnectionAdapterOptions'
        // constructor: ClientCertificateMode.NoCertificate, no validation delegate wired at all.
        httpsOptions.ClientCertificateMode.Should().Be(ClientCertificateMode.NoCertificate);
        httpsOptions.ClientCertificateValidation.Should().BeNull();
    }

    [Fact]
    public void NeitherMtlsExtensionCalled_NoMtlsForwardedHeaderOptionsValidatorIsRegistered()
    {
        var builder = Host.CreateApplicationBuilder();
        using var host = builder.Build();

        host.Services.GetServices<IValidateOptions<MtlsForwardedHeaderOptions>>().Should().BeEmpty();
    }

    [Fact]
    public async Task NeitherMtlsExtensionCalled_HostStartsWithoutThrowing()
    {
        var builder = Host.CreateApplicationBuilder();
        using var host = builder.Build();

        var act = async () => await host.StartAsync();

        await act.Should().NotThrowAsync();
        await host.StopAsync();
    }

    [Fact]
    public void NeitherMtlsExtensionCalled_UnconfiguredMtlsForwardedHeaderOptions_ResolvesWithoutThrowing()
    {
        // Proves resolving IOptions<MtlsForwardedHeaderOptions> is never implicitly fail-fast platform-wide
        // — the validation-on-start behavior is entirely opt-in, attached only by
        // AddMtlsForwardedHeaderCertificate itself (see MtlsForwardedHeaderExtensionsTests' positive-path
        // counterpart).
        var builder = Host.CreateApplicationBuilder();
        using var host = builder.Build();

        var act = () => host.Services.GetRequiredService<IOptions<MtlsForwardedHeaderOptions>>().Value;

        act.Should().NotThrow();
        act().HeaderName.Should().BeEmpty();
    }
}
