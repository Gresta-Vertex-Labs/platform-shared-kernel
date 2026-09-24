using System.Text;
using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Grpc.Core;
using SharedKernel.Presentation.WebApi.Errors;
using RpcStatus = Google.Rpc.Status;

namespace SharedKernel.Presentation.Grpc.Tests.TestSupport;

/// <summary>Reads the <c>google.rpc.Status</c> of an <see cref="RpcException"/> the way a client does.</summary>
internal static class RichStatus
{
    public static RpcStatus ShouldHaveRichStatus(this RpcException exception, StatusCode expectedCode)
    {
        exception.StatusCode.Should().Be(expectedCode);

        var status = exception.GetRpcStatus();
        status.Should().NotBeNull("every error of this package carries a google.rpc.Status");
        status!.Code.Should().Be((int)expectedCode);
        status.Message.Should().Be(exception.Status.Detail);
        return status;
    }

    public static ErrorInfo ErrorInfo(this RpcStatus status)
    {
        var errorInfo = status.GetDetail<ErrorInfo>();
        errorInfo.Should().NotBeNull("every error status carries an ErrorInfo detail");
        return errorInfo!;
    }

    public static IReadOnlyList<(string Field, string Description, string Reason)> FieldViolations(this RpcStatus status)
    {
        var badRequest = status.GetDetail<BadRequest>();
        badRequest.Should().NotBeNull("an error with field errors carries a BadRequest detail");
        return [.. badRequest!.FieldViolations.Select(violation => (violation.Field, violation.Description, violation.Reason))];
    }

    /// <summary>
    /// Returns a copy of <paramref name="status"/> without the ids that differ from one call to the next (the trace
    /// and correlation ids of its <c>ErrorInfo</c>), so the statuses of two calls can be compared as a whole.
    /// </summary>
    public static RpcStatus WithoutRequestIds(this RpcStatus status)
    {
        var copy = status.Clone();

        for (var index = 0; index < copy.Details.Count; index++)
        {
            if (copy.Details[index].TryUnpack<ErrorInfo>(out var errorInfo))
            {
                errorInfo.Metadata.Remove(ProblemDetailsExtensionNames.TraceId);
                errorInfo.Metadata.Remove(ProblemDetailsExtensionNames.CorrelationId);
                copy.Details[index] = Any.Pack(errorInfo);
            }
        }

        return copy;
    }

    /// <summary>
    /// Returns roughly the bytes the status takes on the wire: the detail, and every trailer's name and value, binary
    /// values base64-encoded as HTTP/2 carries them.
    /// </summary>
    public static int StatusBytesOnTheWire(this RpcException exception) =>
        Encoding.UTF8.GetByteCount(exception.Status.Detail)
        + exception.Trailers.Sum(entry =>
            entry.Key.Length + (entry.IsBinary ? Base64Length(entry.ValueBytes.Length) : Encoding.UTF8.GetByteCount(entry.Value)));

    /// <summary>
    /// Returns everything a client can read from <paramref name="exception"/> as text — the status detail, every
    /// trailer, and the rich status in full — so a test can prove a value is nowhere in it.
    /// </summary>
    public static string EverythingTheClientSees(this RpcException exception)
    {
        var text = new StringBuilder(exception.Status.Detail);

        foreach (var entry in exception.Trailers)
        {
            text.Append('\n').Append(entry.Key).Append(": ");
            text.Append(entry.IsBinary ? Encoding.UTF8.GetString(entry.ValueBytes) : entry.Value);
        }

        text.Append('\n').Append(exception.GetRpcStatus()?.ToString());
        return text.ToString();
    }

    private static int Base64Length(int bytes) => (bytes + 2) / 3 * 4;
}
