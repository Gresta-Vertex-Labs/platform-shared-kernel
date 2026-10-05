using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using SharedKernel.Messaging.Abstractions.Options;

namespace SharedKernel.Messaging.MassTransit.Extensions;

/// <summary>
/// Validates <see cref="MessagingOptions.ServiceName"/> at host startup.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why the shape is checked and not just the presence.</strong> The service name is not a
/// label: it is the prefix of every queue and exchange this service declares, and the CloudEvents
/// <c>source</c> on every event it publishes. A name with a space or a capital produces a queue
/// name a broker will either reject or accept-and-mangle, and a <c>source</c> that no subscriber
/// filter matches. Both failures appear far from the configuration that caused them, which is why
/// they are caught here instead.
/// </para>
/// <para>
/// P-561 added the shape check. Before it, the rule existed only in
/// <see cref="MessagingOptions"/>'s own documentation, which promised a lowercase slug was
/// required while nothing enforced it.
/// </para>
/// </remarks>
internal sealed partial class MessagingOptionsValidator : IValidateOptions<MessagingOptions>
{
    /// <summary>
    /// Lowercase alphanumerics in hyphen-separated segments: <c>order-service</c>, <c>billing</c>,
    /// <c>orders-api-v2</c>. No leading, trailing or doubled hyphen, and no underscore, dot, space
    /// or capital.
    /// </summary>
    /// <remarks>
    /// Deliberately stricter than any single broker requires. RabbitMQ would accept a dot and
    /// Azure Service Bus a slash, but a name legal on one transport and not the other turns a
    /// transport switch into a rename of every queue in the deployment.
    /// </remarks>
    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex ServiceNamePattern { get; }

    /// <summary>The longest name accepted, leaving room for the consumer suffix in a queue name.</summary>
    /// <remarks>
    /// Azure Service Bus caps an entity path at 260 characters and RabbitMQ a queue name at 255
    /// bytes. The generated name is <c>{service-name}-{consumer-type}</c>, so the budget is shared
    /// with a type name; 100 leaves the larger half to the part a developer does not choose here.
    /// </remarks>
    private const int MaxServiceNameLength = 100;

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, MessagingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ServiceName))
        {
            return ValidateOptionsResult.Fail(
                $"'{nameof(MessagingOptions.ServiceName)}' must not be null or whitespace. " +
                $"Set it in the '{MessagingOptions.SectionName}' configuration section, or pass " +
                "AddSharedKernelMessaging(o => o.ServiceName = \"my-service\").");
        }

        if (options.ServiceName.Length > MaxServiceNameLength)
        {
            return ValidateOptionsResult.Fail(
                $"'{nameof(MessagingOptions.ServiceName)}' is {options.ServiceName.Length} characters; " +
                $"the maximum is {MaxServiceNameLength}. It is prefixed to every queue name this " +
                "service declares, and the remaining budget belongs to the consumer type name.");
        }

        if (!ServiceNamePattern.IsMatch(options.ServiceName))
        {
            return ValidateOptionsResult.Fail(
                $"'{nameof(MessagingOptions.ServiceName)}' is '{options.ServiceName}', which is not a " +
                "lowercase slug. Use lowercase letters and digits in hyphen-separated segments, for " +
                "example 'order-service'. This value becomes the prefix of every queue and exchange " +
                "this service declares and the CloudEvents 'source' of every event it publishes, so " +
                "an unsupported character fails at the broker or silently stops subscriber filters " +
                "from matching.");
        }

        return ValidateOptionsResult.Success;
    }
}
