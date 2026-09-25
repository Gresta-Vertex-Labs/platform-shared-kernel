namespace SharedKernel.Application.Streaming;

/// <summary>
/// Represents a streaming read that yields a sequence of <typeparamref name="TResponse"/> items.
/// </summary>
/// <typeparam name="TResponse">The raw per-item payload type.</typeparam>
/// <remarks>
/// <para>
/// Platform-vocabulary counterpart to <c>IQuery&lt;TResponse&gt;</c>, but for streaming reads. Sent
/// through <see cref="Messaging.ISender.CreateStream{TResponse}(IStreamQuery{TResponse}, CancellationToken)"/>
/// and handled by an <see cref="IStreamQueryHandler{TQuery,TResponse}"/>. Named consistently with
/// <c>IQuery&lt;TResponse&gt;</c> so a query class self-documents intent.
/// </para>
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
/// <b>Stream behaviors are separate from request behaviors.</b> None of the request pipeline
/// behaviors apply to a stream; a stream runs only the <see cref="IStreamPipelineBehavior{TRequest,TResponse}"/>
/// implementations registered for it, and <c>SharedKernel.Application.Pipeline</c> registers none of
/// its own. Logging, validation and authorization for a stream are the handler's own job unless a
/// service registers a stream behavior for them.
/// </para>
/// </remarks>
public interface IStreamQuery<out TResponse>;
