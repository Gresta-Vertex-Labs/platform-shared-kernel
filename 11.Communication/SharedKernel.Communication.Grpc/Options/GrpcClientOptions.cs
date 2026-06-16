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
    /// </summary>
    public int DeadlineSeconds { get; set; } = 30;

    /// <summary>
    /// Whether to enable gRPC retry policy at the channel level. Default: <c>true</c>.
    /// </summary>
    public bool EnableRetry { get; set; } = true;
}
