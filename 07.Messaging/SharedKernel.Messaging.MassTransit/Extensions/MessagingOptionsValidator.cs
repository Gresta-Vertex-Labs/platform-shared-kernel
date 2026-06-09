using Microsoft.Extensions.Options;
using SharedKernel.Messaging.Abstractions.Options;

namespace SharedKernel.Messaging.MassTransit.Extensions;

/// <summary>Validates that <see cref="MessagingOptions.ServiceName"/> is non-null and non-whitespace.</summary>
internal sealed class MessagingOptionsValidator : IValidateOptions<MessagingOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, MessagingOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ServiceName))
            return ValidateOptionsResult.Fail(
                $"'{nameof(MessagingOptions.ServiceName)}' must not be null or whitespace. " +
                "Configure it in the 'SharedKernel:Messaging' section or via " +
                "AddSharedKernelMessaging(o => o.ServiceName = \"my-service\").");

        return ValidateOptionsResult.Success;
    }
}
