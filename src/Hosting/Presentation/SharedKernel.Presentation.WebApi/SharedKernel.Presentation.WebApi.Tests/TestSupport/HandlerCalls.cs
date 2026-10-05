using System.Collections.Concurrent;

namespace SharedKernel.Presentation.WebApi.Tests.TestSupport;

/// <summary>
/// Records every run of a test endpoint's handler and the header value it saw, so a test can prove that a refusal
/// came before the handler.
/// </summary>
public sealed class HandlerCalls
{
    /// <summary>What a handler reports when it saw no header.</summary>
    public const string None = "(none)";

    private readonly ConcurrentQueue<string> _endpoints = new();

    /// <summary>Records a run of the handler of <paramref name="endpoint"/> and returns what it saw.</summary>
    /// <returns><paramref name="value"/>, or <see cref="None"/> when the handler saw no header.</returns>
    public string Record(string endpoint, string? value)
    {
        _endpoints.Enqueue(endpoint);
        return value ?? None;
    }

    /// <summary>Returns how often the handler of <paramref name="endpoint"/> ran.</summary>
    public int Count(string endpoint) => _endpoints.Count(recorded => recorded == endpoint);
}
