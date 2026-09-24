using System.Globalization;
using System.Net.Mime;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi;
using SharedKernel.Presentation.OpenApi.Routing;
using SharedKernel.Presentation.WebApi.Errors;
using SharedKernel.Presentation.WebApi.Http;
using SharedKernel.Presentation.WebApi.Idempotency;
using SharedKernel.Primitives.Propagation;

namespace SharedKernel.Presentation.OpenApi.Documents;

/// <summary>
/// Documents on every operation what the WebApi core enforces for its endpoint: the problem response of any error,
/// the security requirement and 401/403 of a protected endpoint, and the <c>Idempotency-Key</c> and <c>If-Match</c>
/// headers it requires, with the responses that refuse a request without them.
/// </summary>
/// <remarks>
/// <para>
/// The header requirements are read from the endpoint's metadata, which the core adds alike for a convention
/// (<c>RequireIdempotencyKey()</c>, <c>RequireIfMatch()</c>), an attribute and an <c>IdempotencyKey</c> or
/// <c>IfMatch&lt;TVersion&gt;</c> handler parameter, so all of them are documented alike. The API Explorer does not
/// describe such a parameter itself: a type bound by <c>BindAsync</c> is neither a body nor a query value.
/// </para>
/// <para>Only adds: a response, parameter or security requirement the operation already declares is kept.</para>
/// </remarks>
internal sealed class PlatformOperationTransformer : IOpenApiOperationTransformer
{
    /// <summary>The response key OpenAPI reserves for every status an operation does not list.</summary>
    internal const string DefaultResponseKey = "default";

    private const string IdempotencyKeyDescription =
        "Identifies this request, so that a retry of it is recognized: 1 to 256 visible ASCII characters, optionally "
        + "enclosed in double quotes. A request without a valid key is answered 400.";

    private const string IfMatchDescription =
        "The entity tag of the version this request changes, as the ETag of a read returned it: exactly one strong "
        + "entity tag, such as \"42\".";

    private const string IdempotencyKeyRefused =
        $"The Idempotency-Key header is missing ({PresentationErrorCodes.IdempotencyKeyRequired}) or is not 1 to 256 "
        + $"visible ASCII characters ({PresentationErrorCodes.IdempotencyKeyInvalid}).";

    private const string IfMatchMalformed =
        $"The If-Match header is malformed or names more than one entity tag ({PresentationErrorCodes.PreconditionInvalid}).";

    private const string IfMatchNotCurrent =
        $"The entity tag in If-Match is not the current version: it is weak or not a version of this resource "
        + $"({PresentationErrorCodes.PreconditionFailed}), or the resource changed since it was read (the code of that "
        + "conflict, such as persistence.concurrency_conflict). Read the resource again.";

    private const string IfMatchMissing =
        $"The If-Match header is missing or * ({PresentationErrorCodes.PreconditionRequired}): the request must name "
        + "the version it changes.";

    private readonly SecuritySchemeSet _schemes;

    public PlatformOperationTransformer(SecuritySchemeSet schemes)
    {
        _schemes = schemes;
    }

    /// <inheritdoc />
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var services = context.ApplicationServices;
        var action = context.Description.ActionDescriptor;
        IEnumerable<object> metadata = services.GetService<EndpointMetadataLookup>()?.Find(action) ?? (IEnumerable<object>)action.EndpointMetadata;

        var document = context.Document;
        var responses = operation.Responses ??= new OpenApiResponses();
        var requiresIdempotencyKey = metadata.OfType<IIdempotencyKeyRequiredMetadata>().Any();
        var requiresIfMatch = metadata.OfType<IIfMatchRequiredMetadata>().Any();

        if (requiresIdempotencyKey)
        {
            RequireHeader(
                operation,
                WellKnownHeaders.IdempotencyKey,
                IdempotencyKeyDescription,
                new OpenApiSchema { Type = JsonSchemaType.String, MinLength = 1, MaxLength = 256 });
        }

        if (requiresIfMatch)
        {
            RequireHeader(operation, HeaderNames.IfMatch, IfMatchDescription, new OpenApiSchema { Type = JsonSchemaType.String });
        }

        // The platform's responses follow the operation's own, in status order.
        if (requiresIdempotencyKey || requiresIfMatch)
        {
            responses.TryAdd(
                Key(StatusCodes.Status400BadRequest),
                ProblemResponse(document, DescribeRefusedHeaders(requiresIdempotencyKey, requiresIfMatch)));
        }

        if (EndpointAuthorization.IsAuthorized(metadata, services))
        {
            AddSecurityRequirements(operation, document);

            responses.TryAdd(
                Key(StatusCodes.Status401Unauthorized),
                ProblemResponse(document, "Not authenticated, or the operation needs a more recent or stronger sign-in (see WWW-Authenticate)."));
            responses.TryAdd(
                Key(StatusCodes.Status403Forbidden),
                ProblemResponse(document, "Authenticated, but not allowed to perform this operation."));
        }

        if (requiresIfMatch)
        {
            responses.TryAdd(Key(StatusCodes.Status412PreconditionFailed), ProblemResponse(document, IfMatchNotCurrent));
            responses.TryAdd(Key(StatusCodes.Status428PreconditionRequired), ProblemResponse(document, IfMatchMissing));
        }

        responses.TryAdd(DefaultResponseKey, ProblemResponse(document, "An error, described as RFC 9457 problem details."));

        return Task.CompletedTask;
    }

    /// <summary>Describes the 400 the core answers when a required header is missing or malformed.</summary>
    private static string DescribeRefusedHeaders(bool idempotencyKey, bool ifMatch) => (idempotencyKey, ifMatch) switch
    {
        (true, true) => IdempotencyKeyRefused + " " + IfMatchMalformed,
        (true, false) => IdempotencyKeyRefused,
        _ => IfMatchMalformed,
    };

    private void AddSecurityRequirements(OpenApiOperation operation, OpenApiDocument? document)
    {
        // A reference serializes only when it can reach the document that declares the scheme.
        if (document is null || _schemes.Names.Count == 0 || operation.Security is { Count: > 0 })
        {
            return;
        }

        operation.Security =
        [
            .. _schemes.Names.Select(name => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(name, document)] = [],
            }),
        ];
    }

    private static void RequireHeader(OpenApiOperation operation, string name, string description, OpenApiSchema schema)
    {
        var parameters = operation.Parameters ??= [];

        foreach (var existing in parameters)
        {
            if (existing.In == ParameterLocation.Header && string.Equals(existing.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                // The endpoint binds the header itself; keep its description but state that it is required.
                if (existing is OpenApiParameter parameter)
                {
                    parameter.Required = true;
                    parameter.Description ??= description;
                }

                return;
            }
        }

        parameters.Add(new OpenApiParameter
        {
            Name = name,
            In = ParameterLocation.Header,
            Required = true,
            Description = description,
            Schema = schema,
        });
    }

    private static OpenApiResponse ProblemResponse(OpenApiDocument? document, string description) => new()
    {
        Description = description,
        Content = new Dictionary<string, OpenApiMediaType>(StringComparer.Ordinal)
        {
            [MediaTypeNames.Application.ProblemJson] = new OpenApiMediaType
            {
                Schema = new OpenApiSchemaReference(ProblemDetailsSchema.Id, document),
            },
        },
    };

    private static string Key(int statusCode) => statusCode.ToString(CultureInfo.InvariantCulture);
}
