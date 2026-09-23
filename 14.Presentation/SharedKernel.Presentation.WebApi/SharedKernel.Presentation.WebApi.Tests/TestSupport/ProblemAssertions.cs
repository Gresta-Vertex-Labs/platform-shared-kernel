using System.Text.Json;
using FluentAssertions;
using SharedKernel.Presentation.WebApi.Errors;
using SharedKernel.Primitives.Propagation;

namespace SharedKernel.Presentation.WebApi.Tests.TestSupport;

/// <summary>Asserts the one error shape (design D1) every error response must have.</summary>
internal static class ProblemAssertions
{
    public const string ProblemJson = "application/problem+json";

    /// <summary>
    /// Asserts <paramref name="response"/> is an <c>application/problem+json</c> response with the D1 member set —
    /// <c>type</c>, <c>title</c>, <c>status</c>, <c>instance</c>, <c>errorCode</c>, <c>traceId</c> and
    /// <c>correlationId</c> matching the response header — and returns the parsed body.
    /// </summary>
    public static async Task<JsonElement> ShouldBeProblemAsync(this HttpResponseMessage response, int status, string errorCode)
    {
        var body = await response.Content.ReadAsStringAsync();

        ((int)response.StatusCode).Should().Be(status, body);
        response.Content.Headers.ContentType?.MediaType.Should().Be(ProblemJson, body);

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement.Clone();

        root.GetProperty("status").GetInt32().Should().Be(status);
        root.GetProperty(ProblemDetailsExtensionNames.ErrorCode).GetString().Should().Be(errorCode);

        // B15: title is the status reason phrase, never the code; type is never a third-party site.
        var title = root.GetProperty("title").GetString();
        title.Should().NotBeNullOrWhiteSpace();
        title.Should().NotBe(errorCode);

        var type = root.GetProperty("type").GetString();
        type.Should().NotBeNullOrWhiteSpace();
        type.Should().NotContain("httpstatuses");

        root.GetProperty("instance").GetString().Should().StartWith("/");
        root.GetProperty(ProblemDetailsExtensionNames.TraceId).GetString().Should().NotBeNullOrWhiteSpace();

        var correlationId = root.GetProperty(ProblemDetailsExtensionNames.CorrelationId).GetString();
        correlationId.Should().NotBeNullOrWhiteSpace();
        response.Headers.GetValues(WellKnownHeaders.CorrelationId).Should().ContainSingle().Which.Should().Be(correlationId);

        return root;
    }

    public static string? Detail(this JsonElement problem) =>
        problem.TryGetProperty("detail", out var detail) ? detail.GetString() : null;
}
