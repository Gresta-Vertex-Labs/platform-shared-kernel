using System.ClientModel.Primitives;

namespace SharedKernel.AI.SemanticKernel.Tests.Errors;

/// <summary>
/// A minimal, legitimate <see cref="PipelineResponse"/> implementation used solely to construct a real
/// <see cref="System.ClientModel.ClientResultException"/> instance carrying a specific HTTP status code
/// for <c>SemanticKernelErrors</c> mapping tests — not a reflection-based fabrication.
/// </summary>
internal sealed class FakePipelineResponse : PipelineResponse
{
    private readonly BinaryData _content = BinaryData.FromString(string.Empty);

    public FakePipelineResponse(int status)
    {
        Status = status;
    }

    public override int Status { get; }

    public override string ReasonPhrase => string.Empty;

    public override Stream? ContentStream { get; set; }

    public override BinaryData Content => _content;

    protected override PipelineResponseHeaders HeadersCore => throw new NotSupportedException("Not needed by these tests.");

    public override BinaryData BufferContent(CancellationToken cancellationToken = default) => _content;

    public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default) => new(_content);

    public override void Dispose()
    {
    }
}
