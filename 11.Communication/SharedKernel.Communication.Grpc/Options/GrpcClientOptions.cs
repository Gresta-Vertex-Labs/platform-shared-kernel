using Microsoft.Extensions.Options;

namespace SharedKernel.Communication.Grpc.Options;

/// <summary>
/// Configuration for a typed gRPC client registered via
/// <c>IGrpcCommunicationBuilder.AddGrpcClient&lt;TClient&gt;</c>.
/// </summary>
public sealed class GrpcClientOptions
{
    /// <summary>
    /// Channel address (e.g. <c>"http://order-service:5001"</c>). Required when
    /// <c>IServiceEndpointResolver</c> is not registered.
    /// </summary>
    public string? Address { get; set; }

    /// <summary>
    /// Per-call deadline in seconds applied via <c>CallOptions.Deadline</c>. Default: 30 s.
    /// Must be greater than zero — enforced by <see cref="GrpcClientOptionsValidator"/>.
    /// </summary>
    public int DeadlineSeconds { get; set; } = 30;

    /// <summary>
    /// Whether to enable gRPC retry policy at the channel level. Default: <c>true</c>.
    /// </summary>
    public bool EnableRetry { get; set; } = true;
}

/// <summary>
/// Validates <see cref="GrpcClientOptions"/> at the point of consumption. Rejects
/// <see cref="GrpcClientOptions.DeadlineSeconds"/> values that are zero or negative.
/// Invoked directly by <c>GrpcCommunicationBuilder.AddGrpcClient&lt;TClient&gt;</c> against the
/// just-constructed options instance (P-359/WO-056) — this type is also registered as
/// <see cref="IValidateOptions{TOptions}"/> for any future direct <see cref="IOptions{TOptions}"/>
/// consumer, but that registration alone is not the enforcement mechanism relied upon, since
/// <see cref="GrpcClientOptions"/> is never resolved via <c>IOptions&lt;GrpcClientOptions&gt;.Value</c>.
/// Mirrors <c>RestClientOptionsValidator</c>'s identical validate-at-point-of-consumption pattern.
/// </summary>
internal sealed class GrpcClientOptionsValidator : IValidateOptions<GrpcClientOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, GrpcClientOptions options)
    {
        if (options.DeadlineSeconds <= 0)
        {
            return ValidateOptionsResult.Fail(
                $"GrpcClientOptions.DeadlineSeconds must be greater than zero. Got: {options.DeadlineSeconds}.");
        }

        return ValidateOptionsResult.Success;
    }
}
