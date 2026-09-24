namespace SharedKernel.Presentation.SignalR.Tests.TestSupport;

/// <summary>The error code and message a client read out of a <c>HubException</c> with <c>HubErrorMessage.TryParse</c>.</summary>
/// <param name="Code">The error code, such as <c>order.not_found</c>.</param>
/// <param name="Message">The message after the code.</param>
public sealed record HubError(string Code, string Message);
