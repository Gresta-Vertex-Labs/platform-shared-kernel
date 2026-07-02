namespace SharedKernel.Application.Messaging;

/// <summary>
/// Marks a command as fire-and-forget — the caller does not await the result.
/// </summary>
/// <remarks>
/// <para>
/// Fire-and-forget commands extend <see cref="ICommand"/> (and thus <see cref="ICommandBase"/>)
/// and return <c>Result</c>, but they must <b>never</b> be dispatched via
/// <c>ISender.Send</c>. Instead, route them through
/// <c>IFireAndForgetDispatcher.EnqueueAsync</c>, which places them on a bounded channel for
/// background processing.
/// </para>
/// <para>
/// <c>FireAndForgetGuardBehavior</c> in <c>SharedKernel.Application.Behaviors</c> enforces this
/// at runtime: if a caller mistakenly routes an <see cref="IFireAndForgetCommand"/> through the
/// normal <c>ISender.Send</c> pipeline, the behavior throws
/// <see cref="InvalidOperationException"/> before the handler runs.
/// </para>
/// </remarks>
public interface IFireAndForgetCommand : ICommand;
