using OpenAI;

namespace SharedKernel.AI.SemanticKernel.Raw;

/// <summary>
/// The last-resort escape hatch onto the underlying <see cref="OpenAIClient"/> — registered only when
/// the composition root explicitly opts in via <c>AllowRawClientAccess()</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>THE RAW CLIENT BYPASSES TENANT SCOPING.</b> This package's own adapters carry no tenant concept
/// at all (LLM orchestration and embedding generation are stateless per call), but a raw call made
/// directly against <see cref="Client"/> also bypasses this package's token-usage accounting, retry
/// policy, and error mapping — a caller assumes full responsibility for all of it.
/// </para>
/// <para>
/// This is a SemanticKernel-exclusive contract declared only in this package — referencing it takes a
/// compile-time dependency on <c>SharedKernel.AI.SemanticKernel</c>, never
/// <c>SharedKernel.AI.Abstractions</c>.
/// </para>
/// </remarks>
public interface IKernelRawClientAccessor
{
    /// <summary>Gets the raw <see cref="OpenAIClient"/> instance. THE RAW CLIENT BYPASSES TENANT SCOPING.</summary>
    OpenAIClient Client { get; }
}
