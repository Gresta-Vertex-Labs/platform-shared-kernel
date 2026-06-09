namespace SharedKernel.Messaging.Abstractions.Faults;

/// <summary>
/// Carries the exception details extracted from a dead-lettered MassTransit <c>Fault&lt;T&gt;</c> message.
/// </summary>
/// <remarks>
/// Value equality is provided by record semantics — two instances with identical
/// <see cref="ExceptionType"/> and <see cref="Message"/> are considered equal.
/// This type is AOT-safe: no reflection is used in its construction or equality path.
/// Populated by <c>FaultConsumerAdapter</c> in the MassTransit package from
/// <c>Fault&lt;T&gt;.Exceptions</c>. Application fault handlers consume this type directly
/// — no MassTransit reference is required.
/// </remarks>
/// <param name="ExceptionType">The CLR full name of the exception type (e.g. <c>System.InvalidOperationException</c>).</param>
/// <param name="Message">The exception message text.</param>
public sealed record FaultExceptionInfo(string ExceptionType, string Message);
