using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Cryptography.Symmetric;

namespace SharedKernel.Integration.Webhooks.Options;

/// <summary>
/// The startup rule behind <see cref="WebhookDeliveryOptions.EncryptPayload"/>: encryption needs a registered
/// <see cref="ISymmetricEncryptionService"/>. Checked without resolving anything (<see cref="IServiceProviderIsService"/>),
/// so options validation never builds the cryptography stack; the dispatcher keeps its resolution-time guard as the
/// backstop.
/// </summary>
internal static class WebhookEncryptionRegistrationValidator
{
    /// <summary>The failure message of both the startup check and the resolution-time guard.</summary>
    internal const string MissingServiceMessage =
        "WebhookDeliveryOptions.EncryptPayload is enabled but no ISymmetricEncryptionService is registered. "
        + "Register an IEncryptionKeyProvider and call AddSharedKernelCryptography(configuration).AddSymmetricEncryption().";

    /// <summary>Whether the options are consistent with what the container registers.</summary>
    /// <param name="options">The bound options.</param>
    /// <param name="isService">The container's registration probe.</param>
    internal static bool IsSatisfied(WebhookDeliveryOptions options, IServiceProviderIsService isService) =>
        !options.EncryptPayload || isService.IsService(typeof(ISymmetricEncryptionService));
}
