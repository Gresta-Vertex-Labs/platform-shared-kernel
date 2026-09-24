namespace SharedKernel.Presentation.WebApi.Http;

/// <summary>The <see cref="IETagResponseMetadata"/> an <see cref="OkWithETag{TValue}"/> endpoint carries.</summary>
/// <param name="statusCodes">The status codes of the responses that carry the <c>ETag</c> header.</param>
internal sealed class ETagResponseMetadata(params int[] statusCodes) : IETagResponseMetadata
{
    /// <inheritdoc />
    public IReadOnlyList<int> StatusCodes { get; } = Array.AsReadOnly(statusCodes);
}
