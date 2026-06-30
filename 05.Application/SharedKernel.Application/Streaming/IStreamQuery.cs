using MediatR;

namespace SharedKernel.Application.Streaming;

/// <summary>
/// Represents a streaming read that yields a sequence of <typeparamref name="TResponse"/> items.
/// </summary>
/// <typeparam name="TResponse">The raw per-item payload type.</typeparam>
/// <remarks>
/// Platform-vocabulary counterpart to <c>IQuery&lt;TResponse&gt;</c>, but for streaming reads —
/// built directly on MediatR's own <see cref="IStreamRequest{TResponse}"/> (already part of the
/// pinned MediatR 12.4.x dependency, zero new NuGet package). Named consistently with
/// <c>IQuery&lt;TResponse&gt;</c> so a query class self-documents intent.
/// <para>
/// <b>Deliberate, documented deviation from the <c>Result&lt;T&gt;</c> railway:</b>
/// <typeparamref name="TResponse"/> here is the raw per-item payload type — this interface does
/// <b>not</b> wrap items in <c>Result&lt;TResponse&gt;</c>, neither per-item nor as a terminal
/// wrapper. Streaming semantics (<see cref="IAsyncEnumerable{T}"/>) differ fundamentally from the
/// single-response <c>Result&lt;T&gt;</c> railway used everywhere else in this domain: a stream's
/// natural error channel is a thrown exception that terminates enumeration (standard
/// <see cref="IAsyncEnumerable{T}"/> semantics), not a per-item success/failure union. Wrapping
/// every item in <c>Result&lt;TResponse&gt;</c> would force every consumer (GraphQL subscription
/// resolvers, SignalR streamed responses, cursor exports) to unwrap on every iteration for no
/// benefit, and a single terminal <c>Result&lt;IAsyncEnumerable&lt;T&gt;&gt;</c> cannot represent
/// "the stream started fine but item 4000 failed" — exactly the case that matters. This
/// inconsistency with the rest of the domain is intentional and explicit, not an oversight.
/// </para>
/// <para>
/// <b>No pipeline behavior coverage (explicit, not an oversight):</b> none of this domain's ten
/// pipeline behaviors apply to <see cref="IStreamQuery{TResponse}"/> — MediatR treats unary and
/// streaming requests as two separate generic hierarchies with no shared base. Extending any
/// behavior to streaming is an explicit, deliberate future phase — never silently assumed to
/// already work just because the unary behavior exists.
/// </para>
/// </remarks>
public interface IStreamQuery<TResponse> : IStreamRequest<TResponse>;
