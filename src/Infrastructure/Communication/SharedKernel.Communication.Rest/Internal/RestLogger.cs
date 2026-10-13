using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SharedKernel.Communication.Rest.Internal;

/// <summary>
/// Reaches the logger of the client a response came through. The <c>Result</c> verbs are static extensions over
/// <see cref="HttpClient"/> and <see cref="HttpResponseMessage"/> and hold no container, so the client's propagation
/// handler leaves its logger on <see cref="HttpRequestMessage.Options"/>; a response that did not come through a
/// registered client (a hand-made one) gets a <see cref="NullLogger"/>.
/// </summary>
internal static class RestLogger
{
    internal static readonly HttpRequestOptionsKey<ILogger> Key = new("SharedKernel.Communication.Rest.Logger");

    /// <summary>Puts <paramref name="logger"/> on the request.</summary>
    public static void Attach(HttpRequestMessage request, ILogger logger) => request.Options.Set(Key, logger);

    /// <summary>The logger of the client that sent the request <paramref name="response"/> answers, or a null logger.</summary>
    public static ILogger For(HttpResponseMessage response) =>
        response.RequestMessage is { } request && request.Options.TryGetValue(Key, out ILogger? logger)
            ? logger
            : NullLogger.Instance;
}
