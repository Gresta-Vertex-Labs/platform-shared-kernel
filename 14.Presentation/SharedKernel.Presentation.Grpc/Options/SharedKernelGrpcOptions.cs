using System.ComponentModel.DataAnnotations;
using SharedKernel.Configuration;

namespace SharedKernel.Presentation.Grpc;

/// <summary>
/// Settings for the gRPC services set up by <see cref="GrpcHostBuilderExtensions.AddSharedKernelGrpc"/>, bound from
/// <c>SharedKernel:Presentation:Grpc</c> and validated at startup.
/// </summary>
/// <remarks>
/// The <c>configure</c> callback of <see cref="GrpcHostBuilderExtensions.AddSharedKernelGrpc"/> runs after binding, so
/// code can override configuration.
/// </remarks>
public sealed class SharedKernelGrpcOptions : ISectionBoundOptions
{
    /// <summary>Gets the configuration section these settings bind from: <c>SharedKernel:Presentation:Grpc</c>.</summary>
    public static string SectionName => "SharedKernel:Presentation:Grpc";

    /// <summary>
    /// Gets or sets the <c>domain</c> of the <c>google.rpc.ErrorInfo</c> detail every error status carries: the logical
    /// owner of the error codes this service returns, such as <c>orders.example.com</c>. Defaults to the application
    /// name (<c>IHostEnvironment.ApplicationName</c>). Must not be empty.
    /// </summary>
    /// <remarks>
    /// A client reads the error code as <c>reason</c> and this value as <c>domain</c>; together they identify the error
    /// across every service the client calls, so keep it stable once clients depend on it.
    /// </remarks>
    [Required]
    public string ErrorDomain { get; set; } = string.Empty;
}
