using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using SharedKernel.Presentation.WebApi.Concurrency;
using SharedKernel.Presentation.WebApi.Errors;
using SharedKernel.Primitives.Errors;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Concurrency;

public class ConditionalRequestExtensionsTests
{
    [Fact]
    public void TryValidateIfMatch_NoIfMatchHeader_ReturnsTrue_NoProblemDetails()
    {
        var httpContext = new DefaultHttpContext();
        var currentETag = RowVersionETag.From([1, 2, 3]);

        var isValid = httpContext.TryValidateIfMatch(currentETag, out var problemDetails);

        isValid.Should().BeTrue();
        problemDetails.Should().BeNull();
    }

    [Fact]
    public void TryValidateIfMatch_MatchingIfMatch_ReturnsTrue_NoProblemDetails()
    {
        var httpContext = new DefaultHttpContext();
        var currentETag = RowVersionETag.From([1, 2, 3]);
        httpContext.Request.Headers[HeaderNames.IfMatch] = currentETag;

        var isValid = httpContext.TryValidateIfMatch(currentETag, out var problemDetails);

        isValid.Should().BeTrue();
        problemDetails.Should().BeNull();
    }

    [Fact]
    public void TryValidateIfMatch_WildcardIfMatch_ReturnsTrue_NoProblemDetails()
    {
        var httpContext = new DefaultHttpContext();
        var currentETag = RowVersionETag.From([1, 2, 3]);
        httpContext.Request.Headers[HeaderNames.IfMatch] = "*";

        var isValid = httpContext.TryValidateIfMatch(currentETag, out var problemDetails);

        isValid.Should().BeTrue();
        problemDetails.Should().BeNull();
    }

    [Fact]
    public void TryValidateIfMatch_MismatchedIfMatch_ReturnsFalse_Produces412ProblemDetails()
    {
        var httpContext = new DefaultHttpContext();
        var currentETag = RowVersionETag.From([1, 2, 3]);
        var staleETag = RowVersionETag.From([9, 9, 9]);
        httpContext.Request.Headers[HeaderNames.IfMatch] = staleETag;

        var isValid = httpContext.TryValidateIfMatch(currentETag, out var problemDetails);

        isValid.Should().BeFalse();
        problemDetails.Should().NotBeNull();
        problemDetails!.Status.Should().Be(StatusCodes.Status412PreconditionFailed);
        problemDetails.Type.Should().Be("https://httpstatuses.io/412");
    }

    [Fact]
    public void TryValidateIfMatch_MismatchedIfMatch_PopulatesTraceId()
    {
        var httpContext = new DefaultHttpContext { TraceIdentifier = "trace-412" };
        var currentETag = RowVersionETag.From([1, 2, 3]);
        var staleETag = RowVersionETag.From([9, 9, 9]);
        httpContext.Request.Headers[HeaderNames.IfMatch] = staleETag;

        httpContext.TryValidateIfMatch(currentETag, out var problemDetails);

        problemDetails!.Extensions["traceId"].Should().Be("trace-412");
    }

    [Fact]
    public void ErrorTypeStatusCodeMap_StillResolvesExactlyExistingMappedTypesPlusFallback_NoNewCaseSilentlyAdded()
    {
        // (T-38 — REGRESSION) The ETag/If-Match capability is additive and must never change
        // ErrorTypeStatusCodeMap's own mapping surface.
        var expected = new Dictionary<ErrorType, int>
        {
            [ErrorType.Validation] = StatusCodes.Status400BadRequest,
            [ErrorType.Unauthorized] = StatusCodes.Status401Unauthorized,
            [ErrorType.Forbidden] = StatusCodes.Status403Forbidden,
            [ErrorType.NotFound] = StatusCodes.Status404NotFound,
            [ErrorType.Conflict] = StatusCodes.Status409Conflict,
            [ErrorType.BusinessRule] = StatusCodes.Status422UnprocessableEntity,
            [ErrorType.Unexpected] = StatusCodes.Status500InternalServerError,
        };

        foreach (var (errorType, statusCode) in expected)
        {
            ErrorTypeStatusCodeMap.Resolve(errorType).Should().Be(statusCode);
        }

        var everyNonNoneMember = Enum.GetValues<ErrorType>().Where(type => type != ErrorType.None).ToArray();
        everyNonNoneMember.Should().BeEquivalentTo(expected.Keys);

        ErrorTypeStatusCodeMap.Resolve(ErrorType.None).Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public void ErrorConflict_StillMapsTo409_ViaToProblemDetails_Unaffected()
    {
        // (T-38 — REGRESSION) 06.Persistence's Error.Conflict/409 optimistic-concurrency path
        // remains independently available alongside the new If-Match/412 path.
        var error = Error.Conflict("order.conflict", "Order already shipped.");

        var problemDetails = error.ToProblemDetails();

        problemDetails.Status.Should().Be(StatusCodes.Status409Conflict);
    }
}
