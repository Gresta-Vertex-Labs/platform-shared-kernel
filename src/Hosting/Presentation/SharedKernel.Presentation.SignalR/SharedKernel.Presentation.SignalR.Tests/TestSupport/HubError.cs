namespace SharedKernel.Presentation.SignalR.Tests.TestSupport;

/// <summary>The error code and message a client read out of a <c>HubException</c> with <c>HubErrorMessage.TryParse</c>.</summary>
/// <param name="Code">The error code, such as <c>order.not_found</c>.</param>
/// <param name="Message">The message after the code.</param>
public sealed record HubError(string Code, string Message);

/// <summary>A <see cref="HubError"/> with the ids of the trailer.</summary>
/// <param name="Code">The error code.</param>
/// <param name="Message">The message after the code, without the trailer.</param>
/// <param name="TraceId">The trace id of the failed invocation.</param>
/// <param name="CorrelationId">The correlation id of the connection, or <see langword="null"/> when the trailer has none.</param>
public sealed record HubErrorWithIds(string Code, string Message, string TraceId, string? CorrelationId);
