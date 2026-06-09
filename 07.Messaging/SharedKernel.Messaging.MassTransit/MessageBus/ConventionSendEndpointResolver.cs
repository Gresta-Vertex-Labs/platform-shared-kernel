using MassTransit;
using Microsoft.Extensions.Options;
using SharedKernel.Messaging.Abstractions.MessageBus;
using SharedKernel.Messaging.Abstractions.Options;

namespace SharedKernel.Messaging.MassTransit.MessageBus;

/// <summary>
/// Default convention-based send endpoint resolver.
/// Derives the queue name from MessagingOptions.ServiceName as prefix
/// and the kebab-case type name as suffix.
/// </summary>
internal sealed class ConventionSendEndpointResolver : ISendEndpointResolver
{
    private readonly IOptions<MessagingOptions> _options;

    public ConventionSendEndpointResolver(IOptions<MessagingOptions> options)
    {
        _options = options;
    }

    /// <inheritdoc />
    public string Resolve<T>() where T : class
    {
        var typeName = KebabCaseEndpointNameFormatter.Instance.SanitizeName(typeof(T).Name);
        return $"{_options.Value.ServiceName}-{typeName}";
    }
}
