using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Communication.Rest.Builders;

namespace SharedKernel.Communication.Rest.Extensions;

/// <summary>
/// DI registration entry point for <c>SharedKernel.Communication.Rest</c>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers platform-standard REST communication infrastructure: <see cref="IRestCommunicationBuilder"/>,
    /// <c>CorrelationIdDelegatingHandler</c> (transient), <c>TenantIdDelegatingHandler</c> (transient),
    /// and the STJ <c>ProblemDetailsJsonContext</c>.
    /// </summary>
    /// <returns>
    /// An <see cref="IRestCommunicationBuilder"/> for fluent typed-client registration via
    /// <c>AddRestClient&lt;TClient&gt;</c>.
    /// </returns>
    public static IRestCommunicationBuilder AddSharedKernelRestCommunication(
        this IServiceCollection services)
    {
        // TODO: implement
        throw new NotImplementedException();
    }
}
