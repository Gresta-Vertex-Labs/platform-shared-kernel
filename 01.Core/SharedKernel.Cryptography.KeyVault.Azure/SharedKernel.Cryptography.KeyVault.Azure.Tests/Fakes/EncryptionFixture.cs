using System.Diagnostics;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Random;

namespace SharedKernel.Cryptography.KeyVault.Azure.Tests.Fakes;

internal sealed class EncryptionFixture
{
    public const string MasterKeyName = "orders-kek";
    public const string DataKeySecretName = "orders-data-keys";

    public static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);

    public EncryptionFixture()
    {
        MasterKeyVersion = Vault.AddRsaKey(MasterKeyName);
    }

    public FakeKeyVault Vault { get; } = new();

    public FakeTimeProvider Time { get; } = new();

    public string MasterKeyVersion { get; }

    public string MasterKeyId => $"{MasterKeyName}/{MasterKeyVersion}";

    public static AzureKeyVaultEncryptionOptions CreateOptions(Action<AzureKeyVaultEncryptionOptions>? configure = null)
    {
        var options = new AzureKeyVaultEncryptionOptions
        {
            VaultUri = FakeKeyVault.VaultUri,
            MasterKeyName = MasterKeyName,
            DataKeySecretName = DataKeySecretName,
            RefreshInterval = RefreshInterval,
        };
        configure?.Invoke(options);
        return options;
    }

    public AzureKeyVaultEncryptionKeyProvider CreateProvider(Action<AzureKeyVaultEncryptionOptions>? configure = null) =>
        new(Microsoft.Extensions.Options.Options.Create(CreateOptions(configure)), Vault.KeyClient, Vault.SecretClient, new SecureRandomGenerator(), Time);

    public static async Task WaitUntilAsync(Func<bool> condition)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.Elapsed > TimeSpan.FromSeconds(10))
            {
                throw new TimeoutException("The condition was not met within 10 seconds.");
            }

            await Task.Delay(5);
        }
    }
}
