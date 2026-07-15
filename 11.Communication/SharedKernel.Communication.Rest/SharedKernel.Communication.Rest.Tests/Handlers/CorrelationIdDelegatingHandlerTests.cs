using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Communication.Rest.Handlers;
using SharedKernel.Primitives.Propagation;

namespace SharedKernel.Communication.Rest.Tests.Handlers;

public sealed class CorrelationIdDelegatingHandlerTests
{
    private const string CorrelationHeader = WellKnownHeaders.CorrelationId;

    private static HttpClient BuildClient(HttpMessageHandler stub)
    {
        var handler = new CorrelationIdDelegatingHandler
        {
            InnerHandler = stub
        };
        return new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
    }

    [Fact]
    public async Task SendAsync_WithNoActivity_InjectsGuidFallbackCorrelationId()
    {
        // Arrange
        Activity.Current = null;
        string? captured = null;
        var stub = new CaptureHeaderHandler(CorrelationHeader, h => captured = h);
        var client = BuildClient(stub);

        // Act
        await client.GetAsync("/test");

        // Assert
        captured.Should().NotBeNullOrEmpty();
        Guid.TryParse(captured, out _).Should().BeTrue("fallback should be a valid GUID");
    }

    [Fact]
    public async Task SendAsync_WithActiveActivity_InjectsActivityId()
    {
        // Arrange
        string? captured = null;
        var stub = new CaptureHeaderHandler(CorrelationHeader, h => captured = h);
        var client = BuildClient(stub);

        using var activity = new Activity("test-operation");
        activity.Start();

        try
        {
            // Act
            await client.GetAsync("/test");
        }
        finally
        {
            activity.Stop();
        }

        // Assert
        captured.Should().Be(activity.Id, "should use Activity.Current.Id when a trace is active");
    }

    [Fact]
    public async Task SendAsync_WhenCallerAlreadySetHeader_DoesNotOverwrite()
    {
        // Arrange
        const string callerValue = "caller-set-correlation-id";
        string? captured = null;
        var stub = new CaptureHeaderHandler(CorrelationHeader, h => captured = h);
        var client = BuildClient(stub);

        var request = new HttpRequestMessage(HttpMethod.Get, "/test");
        request.Headers.TryAddWithoutValidation(CorrelationHeader, callerValue);

        // Act
        await client.SendAsync(request);

        // Assert
        captured.Should().Be(callerValue, "handler must not overwrite a caller-supplied header");
    }

    [Fact]
    public async Task SendAsync_NeverThrows_OnAnyInput()
    {
        // Arrange
        var stub = new CaptureHeaderHandler(CorrelationHeader, _ => { });
        var client = BuildClient(stub);

        // Act
        Func<Task> act = () => client.GetAsync("/test");

        // Assert
        await act.Should().NotThrowAsync();
    }
}

/// <summary>Test double that captures a single header value and returns 200 OK.</summary>
internal sealed class CaptureHeaderHandler(string headerName, Action<string> capture) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Headers.TryGetValues(headerName, out var values))
        {
            capture(values.First());
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
