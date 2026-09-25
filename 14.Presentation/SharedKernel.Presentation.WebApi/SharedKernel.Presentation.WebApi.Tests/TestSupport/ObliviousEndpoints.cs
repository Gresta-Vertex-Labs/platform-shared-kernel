#nullable disable

using Microsoft.AspNetCore.Builder;
using SharedKernel.Persistence.Abstractions.Repositories;

namespace SharedKernel.Presentation.WebApi.Tests.TestSupport;

/// <summary>
/// Handlers declared in code compiled without nullable annotations, whose parameters' nullability cannot be told: an
/// <see cref="IdempotencyKey"/> or <see cref="IfMatch{TVersion}"/> parameter there requires its header, the safe reading
/// — the framework itself would bind a missing header to <see langword="null"/>.
/// </summary>
internal static class ObliviousEndpoints
{
    public const string IdempotencyKeyPath = "/idempotent-oblivious";

    public const string IfMatchPath = "/versioned-oblivious";

    public static void Map(WebApplication app)
    {
        app.MapPost(IdempotencyKeyPath, (IdempotencyKey key, HandlerCalls calls) => calls.Record(IdempotencyKeyPath, key?.Value));
        app.MapPut(IfMatchPath, (IfMatch<EntityVersion> ifMatch, HandlerCalls calls) => calls.Record(IfMatchPath, ifMatch?.Version.ToString()));
    }
}
