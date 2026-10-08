using SharedKernel.Cryptography.KeyVault.Azure;

namespace Shop.Billing.Api.Security;

/// <summary>
/// Creates Billing's first data key in Key Vault when there is none. 01.Core's Key Vault encryption keeps its AES data
/// keys as versions of one secret and needs one before it can encrypt (the kernel also seals entity versions with it);
/// a real environment does this once when it is provisioned. The emulator starts empty on every run, so Billing does it
/// at startup, before anything else needs the key. Two replicas racing here add two versions and lose nothing.
/// </summary>
public sealed class DataKeyProvisioning(AzureKeyVaultEncryptionKeyProvider keys) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _ = await keys.GetCurrentKeyAsync(cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // "No enabled data key version": the vault is new.
            _ = await keys.RotateDataKeyAsync(cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
