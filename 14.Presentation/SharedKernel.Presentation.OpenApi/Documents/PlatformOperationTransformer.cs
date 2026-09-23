using System.Globalization;
using System.Net.Mime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi;
using SharedKernel.Presentation.WebApi.Http;
using SharedKernel.Presentation.WebApi.Idempotency;
using SharedKernel.Primitives.Propagation;

namespace SharedKernel.Presentation.OpenApi.Documents;

/// <summary>
/// Documents on every operation what the WebApi core enforces for its endpoint: the problem response of any error,
/// the security requirement and 401/403 of a protected endpoint, and the <c>Idempotency-Key</c> and <c>If-Match</c>
/// headers it requires.
/// </summary>
/// <remarks>
/// Only adds: a response, parameter or security requirement the operation already declares is kept.
/// </remarks>
internal sealed class PlatformOperationTransformer : IOpenApiOperationTransformer
{
    /// <summary>The response key OpenAPI reserves for every status an operation does not list.</summary>
    internal const string DefaultResponseKey = "default";

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

        if (RequiresAuthorization(metadata, services))
        {
            AddSecurityRequirements(operation, document);

            responses.TryAdd(
                Key(StatusCodes.Status401Unauthorized),
                ProblemResponse(document, "Not authenticated, or the operation needs a more recent or stronger sign-in (see WWW-Authenticate)."));
            responses.TryAdd(
                Key(StatusCodes.Status403Forbidden),
                ProblemResponse(document, "Authenticated, but not allowed to perform this operation."));
        }

        if (metadata.OfType<IIdempotencyKeyRequiredMetadata>().Any())
        {
            RequireHeader(
                operation,
                WellKnownHeaders.IdempotencyKey,
                "Identifies this request, so that a retry of it is recognized: 1 to 256 visible ASCII characters, "
                + "optionally enclosed in double quotes. A request without a valid key is answered 400.",
                new OpenApiSchema { Type = JsonSchemaType.String, MinLength = 1, MaxLength = 256 });
        }

        if (metadata.OfType<IIfMatchRequiredMetadata>().Any())
        {
            RequireHeader(
                operation,
                HeaderNames.IfMatch,
                "The entity tag (ETag) of the version being changed, as the read returned it.",
                new OpenApiSchema { Type = JsonSchemaType.String });

            responses.TryAdd(
                Key(StatusCodes.Status412PreconditionFailed),
                ProblemResponse(document, "The resource changed since the version named by If-Match; read it again."));
            responses.TryAdd(
                Key(StatusCodes.Status428PreconditionRequired),
                ProblemResponse(document, "The If-Match header is missing."));
        }

        responses.TryAdd(DefaultResponseKey, ProblemResponse(document, "An error, described as RFC 9457 problem details."));

        return Task.CompletedTask;
    }

    /// <summary>
    /// Mirrors ASP.NET Core's authorization middleware: an endpoint with <see cref="IAllowAnonymous"/> is never
    /// authorized; otherwise it is when it has authorization metadata, or when a fallback policy covers every endpoint.
    /// </summary>
    private static bool RequiresAuthorization(IEnumerable<object> metadata, IServiceProvider services)
    {
        var requiresAuthorization = false;

        foreach (var item in metadata)
        {
            switch (item)
            {
                case IAllowAnonymous:
                    return false;
                case IAuthorizeData or AuthorizationPolicy or IAuthorizationRequirementData:
                    requiresAuthorization = true;
                    break;
            }
        }

        return requiresAuthorization || services.GetService<IOptions<AuthorizationOptions>>()?.Value.FallbackPolicy is not null;
    }

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
