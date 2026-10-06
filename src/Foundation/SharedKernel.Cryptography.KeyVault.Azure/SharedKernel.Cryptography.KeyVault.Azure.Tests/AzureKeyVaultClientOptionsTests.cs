using System.Collections.Concurrent;
using System.Net;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Cryptography.Envelope;
using SharedKernel.Cryptography.Extensions;
using SharedKernel.Cryptography.KeyVault.Azure.Tests.Fakes;
using SharedKernel.Cryptography.Signing;
using SharedKernel.Cryptography.Symmetric;
using Xunit;

namespace SharedKernel.Cryptography.KeyVault.Azure.Tests;

/// <summary>
/// The Key Vault clients take the <see cref="KeyClientOptions"/> and <see cref="SecretClientOptions"/> registered in the
/// container, so a host can change their transport — for example to trust a local emulator's certificate.
/// </summary>
public sealed class AzureKeyVaultClientOptionsTests
{
    [Fact]
    public async Task EncryptionProvider_SecretClientOptionsRegistered_SendsThroughTheirTransport()
    {
        var handler = new RecordingHandler();
        await using ServiceProvider provider = BuildProvider(
            AzureKeyVaultOptionsValidationTests.EncryptionSettings(),
            handler,
            (builder, configuration) => builder.AddAzureKeyVaultEncryption(configuration));

        IEncryptionKeyProvider keys = provider.GetRequiredService<IEncryptionKeyProvider>();
        await Assert.ThrowsAnyAsync<Exception>(async () => await keys.GetCurrentKeyAsync());

        Assert.Contains(handler.Paths, path => path.StartsWith("/secrets/orders-data-keys", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EncryptionProvider_KeyClientOptionsRegistered_SendsThroughTheirTransport()
    {
        var handler = new RecordingHandler();
        await using ServiceProvider provider = BuildProvider(
            AzureKeyVaultOptionsValidationTests.EncryptionSettings(),
            handler,
            (builder, configuration) => builder.AddAzureKeyVaultEncryption(configuration));

        IEnvelopeEncryptionProvider envelope = provider.GetRequiredService<IEnvelopeEncryptionProvider>();
        await Assert.ThrowsAnyAsync<Exception>(async () => (await envelope.GenerateDataKeyAsync()).Dispose());

        Assert.Contains(handler.Paths, path => path.StartsWith("/keys/orders-kek", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SigningProvider_KeyClientOptionsRegistered_SendsThroughTheirTransport()
    {
        var handler = new RecordingHandler();
        await using ServiceProvider provider = BuildProvider(
            AzureKeyVaultOptionsValidationTests.SigningSettings(),
            handler,
            (builder, configuration) => builder.AddAzureKeyVaultSigning(configuration));

        ISigningKeyProvider keys = provider.GetRequiredService<ISigningKeyProvider>();
        await Assert.ThrowsAnyAsync<Exception>(async () => await keys.GetSigningKeyAsync("webhooks"));

        Assert.Contains(handler.Paths, path => path.StartsWith("/keys/webhook-signing", StringComparison.Ordinal));
    }

    private static ServiceProvider BuildProvider(
        Dictionary<string, string?> settings,
        RecordingHandler handler,
        Action<ICryptographyBuilder, IConfiguration> register)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var transport = new HttpClientTransport(new HttpClient(handler));
        var keyOptions = new KeyClientOptions { Transport = transport };
        keyOptions.Retry.MaxRetries = 0;
        var secretOptions = new SecretClientOptions { Transport = transport };
        secretOptions.Retry.MaxRetries = 0;

        var services = new ServiceCollection();
        services.AddSingleton<TokenCredential>(new FakeTokenCredential());
        services.AddSingleton(keyOptions);
        services.AddSingleton(secretOptions);
        register(services.AddSharedKernelCryptography(configuration), configuration);
        return services.BuildServiceProvider();
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly ConcurrentQueue<string> _paths = new();

        public IReadOnlyCollection<string> Paths => _paths;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _paths.Enqueue(request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }
}
