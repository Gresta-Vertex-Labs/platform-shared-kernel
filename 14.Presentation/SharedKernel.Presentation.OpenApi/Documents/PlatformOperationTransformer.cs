using System.Globalization;
using System.Net.Mime;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi;
using SharedKernel.Contracts.Pagination;
using SharedKernel.Presentation.OpenApi.Routing;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Propagation;

namespace SharedKernel.Presentation.OpenApi.Documents;

/// <summary>
/// Documents on every operation what the WebApi core enforces for its endpoint: the problem response of any error,
/// the security requirement and 401/403 of a protected endpoint, the <c>Idempotency-Key</c> and <c>If-Match</c>
/// headers it requires or accepts, the paging query parameters of a <c>Paging</c> or <c>CursorPaging</c> handler
/// parameter, with the responses that refuse a request for them, and the <c>ETag</c> header of the responses that carry
/// one.
/// </summary>
/// <remarks>
/// <para>
/// The header requirements are read from the endpoint's metadata, which the core adds alike for a convention
/// (<c>RequireIdempotencyKey()</c>, <c>RequireIfMatch()</c>), an attribute and an <c>IdempotencyKey</c> or
/// <c>IfMatch&lt;TVersion&gt;</c> handler parameter, so all of them are documented alike; the same holds for a header
/// the endpoint accepts without requiring it (<c>AcceptIdempotencyKey()</c>, <c>AcceptIfMatch()</c>, their attributes,
/// a nullable parameter), documented as an optional parameter with its 400 (and 412 for <c>If-Match</c>), never 428.
/// When both are declared, the requirement wins, as in the core. The API Explorer does not describe such a parameter
/// itself: a type bound by <c>BindAsync</c> is neither a body nor a query value. The responses that carry an
/// <c>ETag</c> are read from the <see cref="IETagResponseMetadata"/> an <c>OkWithETag&lt;T&gt;</c> result adds.
/// </para>
/// <para>Only adds: a response, parameter, header or security requirement the operation already declares is kept.</para>
/// </remarks>
internal sealed class PlatformOperationTransformer : IOpenApiOperationTransformer
{
    /// <summary>The response key OpenAPI reserves for every status an operation does not list.</summary>
    internal const string DefaultResponseKey = "default";

    private const string IfMatchDescription =
        "The entity tag of the version this request changes, as the ETag of a read returned it: exactly one strong "
        + "entity tag, such as \"42\".";

    private const string OptionalIfMatchDescription =
        IfMatchDescription + " Optional: without it the request is unconditional.";

    private const string ETagDescription =
        "The version of the resource, as a strong entity tag such as \"42\". Send it in If-None-Match to have a read of "
        + "the unchanged resource answered 304, or in If-Match to change this version.";

    private const string IfMatchMalformed =
        $"The If-Match header is malformed or names more than one entity tag ({PresentationErrorCodes.PreconditionInvalid}).";

    private const string OptionalIfMatchMalformed =
        $"The If-Match header is malformed, names more than one entity tag, or is * "
        + $"({PresentationErrorCodes.PreconditionInvalid}).";

    private const string IfMatchNotCurrent =
        $"The entity tag in If-Match is not the current version: it is weak or not a version of this resource "
        + $"({PresentationErrorCodes.PreconditionFailed}), or the resource changed since it was read (the code of that "
        + "conflict, such as persistence.concurrency_conflict). Read the resource again.";

    private const string IfMatchMissing =
        $"The If-Match header is missing or * ({PresentationErrorCodes.PreconditionRequired}): the request must name "
        + "the version it changes.";

    /// <summary>A key: 1 to <see cref="IdempotencyKey.MaxLength"/> visible ASCII characters (0x21–0x7E).</summary>
    private static readonly string IdempotencyKeyCharacters =
        string.Create(CultureInfo.InvariantCulture, $"[!-~]{{1,{IdempotencyKey.MaxLength}}}");

    /// <summary>
    /// The <c>pattern</c> of the <c>Idempotency-Key</c> schema, admitting exactly the values the core accepts: a key in
    /// one pair of double quotes (two characters longer than the key) or a key alone, but never <c>""</c>, which
    /// encloses no key. A JSON Schema pattern is an ECMA-262 regular expression.
    /// </summary>
    private static readonly string IdempotencyKeyPattern =
        $"^(?!\"\"$)(?:\"{IdempotencyKeyCharacters}\"|{IdempotencyKeyCharacters})$";

    private static readonly string IdempotencyKeyDescription = string.Create(
        CultureInfo.InvariantCulture,
        $"Identifies this request, so that a retry of it is recognized: 1 to {IdempotencyKey.MaxLength} visible ASCII "
        + $"characters, optionally enclosed in double quotes. A request without a valid key is answered 400.");

    private static readonly string OptionalIdempotencyKeyDescription = string.Create(
        CultureInfo.InvariantCulture,
        $"Identifies this request, so that a retry of it is recognized: 1 to {IdempotencyKey.MaxLength} visible ASCII "
        + $"characters, optionally enclosed in double quotes. Optional: a request may leave it out, but a key that is "
        + $"not valid is answered 400.");

    private static readonly string IdempotencyKeyRefused = string.Create(
        CultureInfo.InvariantCulture,
        $"The Idempotency-Key header is missing ({PresentationErrorCodes.IdempotencyKeyRequired}) or is not 1 to "
        + $"{IdempotencyKey.MaxLength} visible ASCII characters ({PresentationErrorCodes.IdempotencyKeyInvalid}).");

    private static readonly string OptionalIdempotencyKeyRefused = string.Create(
        CultureInfo.InvariantCulture,
        $"The Idempotency-Key header is sent but is not 1 to {IdempotencyKey.MaxLength} visible ASCII characters "
        + $"({PresentationErrorCodes.IdempotencyKeyInvalid}).");

    private static readonly string PagingRefused =
        $"A paging query parameter is invalid (errorCode {ErrorCodes.Validation.Failed}; errors and errorCodes keyed by "
        + $"{PagingQuery.Page} and {PagingQuery.PageSize}): out of range ({PaginationErrorCodes.PageOutOfRange}, "
        + $"{PaginationErrorCodes.PageSizeOutOfRange}) or not one whole number ({ErrorCodes.Validation.InvalidFormat}).";

    private static readonly string CursorPagingRefused =
        $"A paging query parameter is invalid (errorCode {ErrorCodes.Validation.Failed}; errors and errorCodes keyed by "
        + $"{PagingQuery.Cursor} and {PagingQuery.Limit}): a cursor that is not one of this API's "
        + $"({PaginationErrorCodes.CursorInvalid}), a limit out of range ({PaginationErrorCodes.LimitOutOfRange}), or a "
        + $"limit that is not one whole number or a parameter sent twice ({ErrorCodes.Validation.InvalidFormat}).";

    private const string PageDescription = "The 1-based page number. Optional: page 1 when absent.";

    private static readonly string PageSizeDescription = string.Create(
        CultureInfo.InvariantCulture,
        $"The number of items per page, 1 to {PageRequest.MaxPageSize}. Optional: {PageRequest.DefaultPageSize} when absent.");

    private static readonly string CursorDescription = string.Create(
        CultureInfo.InvariantCulture,
        $"The opaque cursor the previous page returned as nextCursor, at most {PageCursor.MaxLength} characters. "
        + $"Optional: the first page when absent.");

    private static readonly string LimitDescription = string.Create(
        CultureInfo.InvariantCulture,
        $"The maximum number of items to return, 1 to {CursorPageRequest.MaxLimit}. Optional: "
        + $"{CursorPageRequest.DefaultLimit} when absent.");

    private readonly SecuritySchemeSet _schemes;

    public PlatformOperationTransformer(SecuritySchemeSet schemes)
    {
        _schemes = schemes;
    }

    /// <summary>How an endpoint takes a header, as its metadata declares it.</summary>
    private enum HeaderUse
    {
        None,
        Accepted,
        Required,
    }

    /// <inheritdoc />
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var services = context.ApplicationServices;
        var action = context.Description.ActionDescriptor;
        IEnumerable<object> metadata = services.GetService<EndpointMetadataLookup>()?.Find(action) ?? (IEnumerable<object>)action.EndpointMetadata;

        var document = context.Document;
        var responses = operation.Responses ??= new OpenApiResponses();
        var idempotencyKey = GetUse<IIdempotencyKeyRequiredMetadata, IIdempotencyKeyAcceptedMetadata>(metadata);
        var ifMatch = GetUse<IIfMatchRequiredMetadata, IIfMatchAcceptedMetadata>(metadata);

        if (idempotencyKey != HeaderUse.None)
        {
            AddHeader(
                operation,
                WellKnownHeaders.IdempotencyKey,
                idempotencyKey == HeaderUse.Required,
                idempotencyKey == HeaderUse.Required ? IdempotencyKeyDescription : OptionalIdempotencyKeyDescription,
                new OpenApiSchema { Type = JsonSchemaType.String, Pattern = IdempotencyKeyPattern });
        }

        if (ifMatch != HeaderUse.None)
        {
            AddHeader(
                operation,
                HeaderNames.IfMatch,
                ifMatch == HeaderUse.Required,
                ifMatch == HeaderUse.Required ? IfMatchDescription : OptionalIfMatchDescription,
                new OpenApiSchema { Type = JsonSchemaType.String });
        }

        var paging = metadata.OfType<PagingMetadata>().FirstOrDefault();
        if (paging is not null)
        {
            AddPagingParameters(operation, paging);
        }

        // The platform's responses follow the operation's own, in status order.
        if (idempotencyKey != HeaderUse.None || ifMatch != HeaderUse.None || paging is not null)
        {
            responses.TryAdd(
                Key(StatusCodes.Status400BadRequest),
                ProblemResponse(document, DescribeRefusedInput(idempotencyKey, ifMatch, paging)));
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

        if (ifMatch != HeaderUse.None)
        {
            responses.TryAdd(Key(StatusCodes.Status412PreconditionFailed), ProblemResponse(document, IfMatchNotCurrent));
        }

        // Only a required If-Match is refused for being missing; an accepted one may be left out.
        if (ifMatch == HeaderUse.Required)
        {
            responses.TryAdd(Key(StatusCodes.Status428PreconditionRequired), ProblemResponse(document, IfMatchMissing));
        }

        foreach (var statusCode in metadata.OfType<IETagResponseMetadata>().SelectMany(eTag => eTag.StatusCodes).Distinct())
        {
            AddETagHeader(responses, statusCode);
        }

        responses.TryAdd(DefaultResponseKey, ProblemResponse(document, "An error, described as RFC 9457 problem details."));

        return Task.CompletedTask;
    }

    /// <summary>Reads how an endpoint takes a header: a requirement wins over an acceptance, as in the core.</summary>
    private static HeaderUse GetUse<TRequired, TAccepted>(IEnumerable<object> metadata)
    {
        if (metadata.OfType<TRequired>().Any())
        {
            return HeaderUse.Required;
        }

        return metadata.OfType<TAccepted>().Any() ? HeaderUse.Accepted : HeaderUse.None;
    }

    /// <summary>Declares the <c>ETag</c> header on the response of <paramref name="statusCode"/>, when the operation documents one.</summary>
    private static void AddETagHeader(OpenApiResponses responses, int statusCode)
    {
        // The header describes a response the operation already lists (from the result type); it never adds one.
        if (!responses.TryGetValue(Key(statusCode), out var response) || response is not OpenApiResponse documented)
        {
            return;
        }

        documented.Headers ??= new Dictionary<string, IOpenApiHeader>(StringComparer.OrdinalIgnoreCase);
        documented.Headers.TryAdd(HeaderNames.ETag, new OpenApiHeader
        {
            Description = ETagDescription,
            Required = true,
            Schema = new OpenApiSchema { Type = JsonSchemaType.String },
        });
    }

    /// <summary>
    /// Describes the 400 the core answers when a required or accepted header is missing or malformed, or a paging
    /// query parameter is invalid.
    /// </summary>
    private static string DescribeRefusedInput(HeaderUse idempotencyKey, HeaderUse ifMatch, PagingMetadata? paging)
    {
        List<string> refusals = new(3);

        if (idempotencyKey != HeaderUse.None)
        {
            refusals.Add(idempotencyKey == HeaderUse.Required ? IdempotencyKeyRefused : OptionalIdempotencyKeyRefused);
        }

        if (ifMatch != HeaderUse.None)
        {
            refusals.Add(ifMatch == HeaderUse.Required ? IfMatchMalformed : OptionalIfMatchMalformed);
        }

        if (paging is not null)
        {
            refusals.Add(paging.IsCursor ? CursorPagingRefused : PagingRefused);
        }

        return string.Join(' ', refusals);
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

    /// <summary>
    /// Declares the optional query parameters a <c>Paging</c> (<c>page</c>, <c>pageSize</c>) or <c>CursorPaging</c>
    /// (<c>cursor</c>, <c>limit</c>) handler parameter binds, with the ranges the core enforces.
    /// </summary>
    private static void AddPagingParameters(OpenApiOperation operation, PagingMetadata paging)
    {
        if (paging.IsCursor)
        {
            AddQuery(operation, PagingQuery.Cursor, CursorDescription, new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                MaxLength = PageCursor.MaxLength,
            });
            AddQuery(operation, PagingQuery.Limit, LimitDescription, IntegerSchema(CursorPageRequest.MaxLimit, CursorPageRequest.DefaultLimit));
            return;
        }

        AddQuery(operation, PagingQuery.Page, PageDescription, IntegerSchema(maximum: null, defaultValue: 1));
        AddQuery(operation, PagingQuery.PageSize, PageSizeDescription, IntegerSchema(PageRequest.MaxPageSize, PageRequest.DefaultPageSize));
    }

    private static OpenApiSchema IntegerSchema(int? maximum, int defaultValue) => new()
    {
        Type = JsonSchemaType.Integer,
        Format = "int32",
        Minimum = "1",
        Maximum = maximum?.ToString(CultureInfo.InvariantCulture),
        Default = JsonValue.Create(defaultValue),
    };

    private static void AddQuery(OpenApiOperation operation, string name, string description, OpenApiSchema schema)
    {
        var parameters = operation.Parameters ??= [];

        // A query parameter the endpoint declares itself, under the same name, is kept as it is.
        if (parameters.Any(existing => existing.In == ParameterLocation.Query && string.Equals(existing.Name, name, StringComparison.Ordinal)))
        {
            return;
        }

        parameters.Add(new OpenApiParameter
        {
            Name = name,
            In = ParameterLocation.Query,
            Required = false,
            Description = description,
            Schema = schema,
        });
    }

    private static void AddHeader(OpenApiOperation operation, string name, bool required, string description, OpenApiSchema schema)
    {
        var parameters = operation.Parameters ??= [];

        foreach (var existing in parameters)
        {
            if (existing.In == ParameterLocation.Header && string.Equals(existing.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                // The endpoint binds the header itself; keep its description, and state that it is required when it is.
                if (existing is OpenApiParameter parameter)
                {
                    parameter.Required |= required;
                    parameter.Description ??= description;
                }

                return;
            }
        }

        parameters.Add(new OpenApiParameter
        {
            Name = name,
            In = ParameterLocation.Header,
            Required = required,
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
