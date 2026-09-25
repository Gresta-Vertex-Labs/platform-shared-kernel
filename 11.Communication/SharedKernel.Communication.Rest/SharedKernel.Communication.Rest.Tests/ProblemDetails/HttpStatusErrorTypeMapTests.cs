using SharedKernel.Communication.Rest.ProblemDetails;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Communication.Rest.Tests.ProblemDetails;

/// <summary>
/// <see cref="HttpStatusErrorTypeMap"/> row by row: the reverse of <c>14.Presentation</c>'s
/// <c>ErrorTypeStatusCodeMap</c>, plus the statuses an HTTP boundary answers outside it.
/// </summary>
public sealed class HttpStatusErrorTypeMapTests
{
    [Theory]
    [InlineData(400, ErrorType.Validation)]
    [InlineData(401, ErrorType.Unauthorized)]
    [InlineData(403, ErrorType.Forbidden)]
    [InlineData(404, ErrorType.NotFound)]
    [InlineData(409, ErrorType.Conflict)]
    [InlineData(412, ErrorType.Conflict)]
    [InlineData(413, ErrorType.Validation)]
    [InlineData(415, ErrorType.Validation)]
    [InlineData(422, ErrorType.BusinessRule)]
    [InlineData(428, ErrorType.Validation)]
    [InlineData(429, ErrorType.Unavailable)]
    [InlineData(503, ErrorType.Unavailable)]
    [InlineData(504, ErrorType.Timeout)]
    public void Resolve_MappedStatus_ReturnsItsErrorType(int statusCode, ErrorType expected)
        => HttpStatusErrorTypeMap.Resolve(statusCode).Should().Be(expected);

    [Theory]
    [InlineData(0)]
    [InlineData(405)]
    [InlineData(408)]
    [InlineData(418)]
    [InlineData(500)]
    [InlineData(501)]
    [InlineData(502)]
    [InlineData(599)]
    public void Resolve_UnmappedStatus_FallsBackToUnexpected(int statusCode)
        => HttpStatusErrorTypeMap.Resolve(statusCode).Should().Be(ErrorType.Unexpected);

    [Fact]
    public void Resolve_ServerFaults_StayDistinctFromOutages()
    {
        // P-562: 500 is a defect the caller cannot act on; 503/504 are outages it can retry. The map
        // must keep them apart, or a down dependency reads as a bug in every dashboard.
        HttpStatusErrorTypeMap.Resolve(500).Should().Be(ErrorType.Unexpected);
        HttpStatusErrorTypeMap.Resolve(503).Should().NotBe(ErrorType.Unexpected);
        HttpStatusErrorTypeMap.Resolve(504).Should().NotBe(ErrorType.Unexpected);
    }
}
