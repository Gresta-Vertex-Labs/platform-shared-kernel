using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Cryptography.Symmetric;

namespace SharedKernel.Persistence.EfCore.Integration.Tests;

/// <summary>
/// Test-only root keys for entity versions (P-562 X4): a service registers its own key provider, from which the
/// persistence layer derives the subkey versions are sealed with.
/// </summary>
internal static class TestEntityVersionKeys
{
    /// <summary>The key id of <see cref="Provider"/>.</summary>
    public const string KeyId = "version-test-1";

    /// <summary>An in-memory provider with one 32-byte key.</summary>
    public static readonly StaticEncryptionKeyProvider Provider =
        new(KeyId, [new CryptographicKey(KeyId, Enumerable.Repeat((byte)0x5A, 32).ToArray())]);

    /// <summary>Registers <see cref="Provider"/> as the service's key provider.</summary>
    public static IServiceCollection AddTestEntityVersionKeys(this IServiceCollection services) =>
        services.AddSingleton<ISynchronousEncryptionKeyProvider>(Provider);
}
