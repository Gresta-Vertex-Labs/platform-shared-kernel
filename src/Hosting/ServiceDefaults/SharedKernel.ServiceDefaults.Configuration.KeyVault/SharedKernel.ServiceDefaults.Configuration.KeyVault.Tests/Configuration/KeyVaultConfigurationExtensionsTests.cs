using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Security.KeyVault.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using SharedKernel.ServiceDefaults.Configuration;

namespace SharedKernel.ServiceDefaults.Configuration.KeyVault.Tests.Configuration;

/// <summary>
/// Covers WO-061/P-398's GATING acceptance criterion for <c>AddSharedKernelKeyVaultConfiguration</c>:
/// an unreachable vault must cause the call itself to throw, never silently proceed with an empty
/// configuration source.
/// </summary>
public sealed class KeyVaultConfigurationExtensionsTests
{
    [Fact]
    public void AddSharedKernelKeyVaultConfiguration_NullBuilder_ThrowsArgumentNullException()
    {
        IHostApplicationBuilder builder = null!;

        var act = () => builder.AddSharedKernelKeyVaultConfiguration(new Uri("https://my-vault.vault.azure.net/"));

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddSharedKernelKeyVaultConfiguration_NullVaultUri_ThrowsArgumentNullException()
    {
        var builder = Host.CreateApplicationBuilder();

        var act = () => builder.AddSharedKernelKeyVaultConfiguration(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddSharedKernelKeyVaultConfiguration_UnreachableVault_ThrowsBeforeReturning()
    {
        // Empirical GATING proof: a loopback address with no listener fails fast (connection
        // refused) rather than a slow DNS timeout — a fake, instantly-resolving TokenCredential
        // isolates this test to the connectivity behavior itself, not credential-acquisition
        // latency. Confirms ConfigurationManager's eager, synchronous provider-Load() behavior
        // documented on AddSharedKernelKeyVaultConfiguration's own XML docs — the call itself
        // throws, never silently proceeding with an empty configuration source.
        var builder = Host.CreateApplicationBuilder();
        var unreachableVaultUri = new Uri("https://127.0.0.1:1/");

        var act = () => builder.AddSharedKernelKeyVaultConfiguration(unreachableVaultUri, new InstantFakeTokenCredential());

        act.Should().Throw<Exception>();
    }

    [Fact]
    public void AddSharedKernelKeyVaultConfiguration_UnreachableVault_WholeBuildPipelineThrows_NeverProceedsToBuild()
    {
        // T-60's GATING acceptance criterion, phrased literally: a vault URI pointed at an
        // unreachable endpoint must cause IHostApplicationBuilder.Build() to never silently
        // proceed with an empty configuration source. C-56 found the fail-fast mechanism in place
        // (ConfigurationManager's eager/synchronous provider-Load() behavior) fires even earlier,
        // from the Add call itself — a strictly stronger guarantee than "Build() throws", since
        // Build() is never even reached. This test proves that literal end-to-end shape: wrapping
        // both the Add call and Build() in one act, Build() genuinely never executes.
        var builder = Host.CreateApplicationBuilder();
        var unreachableVaultUri = new Uri("https://127.0.0.1:1/");
        var buildWasReached = false;

        var act = () =>
        {
            builder.AddSharedKernelKeyVaultConfiguration(unreachableVaultUri, new InstantFakeTokenCredential());
            buildWasReached = true;
            builder.Build();
        };

        act.Should().Throw<Exception>();
        buildWasReached.Should().BeFalse();
    }

    [Fact]
    public void AddSharedKernelKeyVaultConfiguration_NullClientOptions_ThrowsArgumentNullException()
    {
        var builder = Host.CreateApplicationBuilder();

        var act = () => builder.AddSharedKernelKeyVaultConfiguration(
            new Uri("https://my-vault.vault.azure.net/"), new InstantFakeTokenCredential(), null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddSharedKernelKeyVaultConfiguration_WithClientOptions_ReadsSecretsThroughTheirTransport()
    {
        // The client options' transport is what reaches the vault, so a host can trust a local emulator's
        // certificate. The secret name's "--" becomes the configuration key separator, as with the plain overload.
        var handler = new VaultHandler();
        var clientOptions = new SecretClientOptions { Transport = new HttpClientTransport(new HttpClient(handler)) };
        clientOptions.Retry.MaxRetries = 0;
        var builder = Host.CreateApplicationBuilder();

        builder.AddSharedKernelKeyVaultConfiguration(
            new Uri("https://my-vault.vault.azure.net/"), new InstantFakeTokenCredential(), clientOptions);

        builder.Configuration["Shop:Greeting"].Should().Be("merhaba");
        handler.Requests.Should().Contain(path => path.StartsWith("/secrets", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AddSharedKernelKeyVaultConfiguration_NeverCalled_HostBuildsAndStartsWithoutAnyBehaviorChange()
    {
        // T-61 no-op regression: a host that never calls AddSharedKernelKeyVaultConfiguration() has
        // no configuration-source/behavior change — the new Azure.Extensions.AspNetCore
        // .Configuration.Secrets / Azure.Identity PackageReferences (S-20) are compile-time-only,
        // with zero runtime side effect when this method is simply never invoked.
        var builder = Host.CreateApplicationBuilder();
        using var host = builder.Build();

        var exception = await Record.ExceptionAsync(() => host.StartAsync());

        exception.Should().BeNull();
        await host.StopAsync();
    }

    /// <summary>A vault holding one secret, <c>Shop--Greeting</c>, answering the list and get calls.</summary>
    private sealed class VaultHandler : HttpMessageHandler
    {
        private const string Vault = "https://my-vault.vault.azure.net";

        public ConcurrentQueue<string> Requests { get; } = new();

        // The configuration provider loads synchronously, so the Azure pipeline calls Send, not SendAsync.
        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;
            Requests.Enqueue(path);

            string body = path.TrimEnd('/') == "/secrets"
                ? $$$"""{"value":[{"id":"{{{Vault}}}/secrets/Shop--Greeting","attributes":{"enabled":true}}],"nextLink":null}"""
                : $$$"""{"value":"merhaba","id":"{{{Vault}}}/secrets/Shop--Greeting/0123456789abcdef0123456789abcdef","attributes":{"enabled":true}}""";

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Send(request, cancellationToken));
    }

    /// <summary>
    /// A <see cref="TokenCredential"/> returning a fixed token instantly — isolates the
    /// connectivity-failure test above from <c>DefaultAzureCredential</c>'s multi-source probing
    /// latency (Environment, Managed Identity, Azure CLI, etc.), which is irrelevant to what this
    /// test proves.
    /// </summary>
    private sealed class InstantFakeTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("fake-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new(GetToken(requestContext, cancellationToken));
    }
}
