using CatalogApi.Features.Operations;
using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Primitives.Errors;

namespace CatalogApi;

/// <summary>One index provider's answer to <c>GET /ops/verify</c>.</summary>
/// <param name="Ok">Whether every index of the provider matches this build's definitions.</param>
/// <param name="Error">The error code of the mismatch, when there is one.</param>
/// <param name="Detail">What differs, as a client may see it.</param>
public sealed record IndexVerification(bool Ok, string? Error, string? Detail);

/// <summary>
/// Provisioning, seeding and diagnostics — what a deployment pipeline and an operator would call, rather than what a
/// user would. Each endpoint sends a command or query (<c>Features/Operations</c>).
/// </summary>
/// <remarks>
/// The provision and verify reports list per-index outcomes in their own bodies, so they show each error's message
/// through <see cref="ErrorProblemDetailsExtensions.ToProblemDetails"/> (its <c>detail</c>) — the same text a problem
/// response carries: a definition conflict in full, an engine outage (whose message names internal endpoints) only in
/// Development. That is presentation, so it happens here, not in the handlers.
/// </remarks>
public sealed class OperationsEndpoints : IEndpointModule
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var ops = app.MapGroup("/ops").WithTags("Operations");

        ops.MapPost("/provision", (HttpContext http, ISender sender, CancellationToken ct) =>
            sender.Send(new ProvisionIndexes(), ct).ToOk(outcomes => outcomes.Select(o => new
            {
                provider = o.Provider,
                index = o.Index,
                ok = o.Error is null,
                error = o.Error?.Code,
                message = o.Error is { } error ? error.ToProblemDetails(http).Detail : null,
            })));

        ops.MapDelete("/indexes", (ISender sender, CancellationToken ct) =>
            sender.Send(new DeleteIndexes(), ct).ToOk());

        ops.MapPost("/seed", (ISender sender, CancellationToken ct) =>
            sender.Send(new SeedCatalog(), ct).ToOk());

        // A report rather than one error: 200 when everything matches, 503 — failing a deployment gate — with every
        // provider's outcome when something does not.
        ops.MapGet("/verify", (HttpContext http, ISender sender, CancellationToken ct) =>
            sender.Send(new VerifyIndexes(), ct).ToHttpResult(errors => Report([.. errors.Select(error => Verification(error, http))])));

        ops.MapGet("/probe/{providerKey}/{indexName}", (string providerKey, string indexName, ISender sender, CancellationToken ct) =>
            sender.Send(new ProbeIndex(providerKey, indexName), ct).ToOk());

        app.MapGet("/diagnostics/telemetry", (ISender sender, CancellationToken ct) =>
            sender.Send(new GetTelemetry(), ct).ToOk()).WithTags("Operations");
    }

    private static IndexVerification Verification(Error? error, HttpContext http) => error is null
        ? new IndexVerification(Ok: true, Error: null, Detail: null)
        : new IndexVerification(Ok: false, error.Code, error.ToProblemDetails(http).Detail);

    private static Results<Ok<List<IndexVerification>>, JsonHttpResult<List<IndexVerification>>> Report(List<IndexVerification> outcomes) =>
        outcomes.TrueForAll(outcome => outcome.Ok)
            ? TypedResults.Ok(outcomes)
            : TypedResults.Json(outcomes, statusCode: StatusCodes.Status503ServiceUnavailable);
}
