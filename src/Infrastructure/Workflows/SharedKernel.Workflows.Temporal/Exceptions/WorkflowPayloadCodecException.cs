using SharedKernel.Core.Exceptions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Workflows.Temporal.Errors;

namespace SharedKernel.Workflows.Temporal.Exceptions;

/// <summary>
/// Thrown by the payload encryption codec when a payload cannot be encoded or decoded: a malformed encrypted payload,
/// a failed decryption (tampered ciphertext, wrong or unknown key, mismatched associated data) or a failed encryption.
/// </summary>
/// <remarks>
/// <para>
/// A codec runs inside the Temporal SDK, where no <see cref="SharedKernel.Primitives.Results.Result"/> can flow, so
/// the failure is an exception. It is a <see cref="SharedKernelException"/> carrying
/// <see cref="WorkflowErrorCodes.PayloadCodecFailure"/>, so an edge that sees it translates it by its
/// <see cref="SharedKernelException.Error"/> type instead of answering 500. The message is a fixed sentence: it names
/// no key id, ciphertext or cryptographic detail, which stay on the inner exception.
/// </para>
/// <para>
/// The codec never passes ciphertext through as plaintext: a payload that carries its encoding marker either decodes
/// or raises this exception.
/// </para>
/// </remarks>
public sealed class WorkflowPayloadCodecException : SharedKernelException
{
    /// <summary>Initializes a new <see cref="WorkflowPayloadCodecException"/> carrying <paramref name="error"/>.</summary>
    /// <param name="error">The structured error describing the codec failure.</param>
    public WorkflowPayloadCodecException(Error error)
        : base(error)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="WorkflowPayloadCodecException"/> carrying <paramref name="error"/> and wrapping
    /// <paramref name="innerException"/>.
    /// </summary>
    /// <param name="error">The structured error describing the codec failure.</param>
    /// <param name="innerException">The cryptographic or parsing exception that caused the failure.</param>
    public WorkflowPayloadCodecException(Error error, Exception innerException)
        : base(error, innerException)
    {
    }
}
