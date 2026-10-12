using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using OpenAI;
using SharedKernel.AI.SemanticKernel.Errors;
using SharedKernel.AI.SemanticKernel.Tests.Support;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.AI.SemanticKernel.Tests.Errors;

/// <summary>
/// The classify-or-rethrow table of <see cref="SemanticKernelFaults"/> over directly constructed exception shapes (the
/// real-connector shapes are covered by the fault matrix), and the pin of the SDK default the registration overrides.
/// </summary>
public sealed class SemanticKernelFaultsTests
{
    private const string Sentinel = "host-sentinel.internal body-sentinel api-key-sentinel";

    private static ClientResultException ClientResult(int status) => new(new FakePipelineResponse(status), innerException: null);

    private static Error? Classify(Exception exception, out bool matched, CancellationToken token = default)
    {
        matched = SemanticKernelFaults.TryClassify(exception, token, "semantickernel", "complete", "gpt-test", out var error);
        return error;
    }

    public static TheoryData<int, string, ErrorType> Statuses => new()
    {
        { 400, "intelligence.completion_failed", ErrorType.Unexpected },
        { 401, "intelligence.unauthorized", ErrorType.Unauthorized },
        { 403, "intelligence.unauthorized", ErrorType.Unauthorized },
        { 404, "intelligence.model_not_found", ErrorType.NotFound },
        { 408, "intelligence.timeout", ErrorType.Timeout },
        { 422, "intelligence.completion_failed", ErrorType.Unexpected },
        { 429, "intelligence.rate_limited", ErrorType.Unavailable },
        { 500, "intelligence.engine_fault", ErrorType.Unexpected },
        { 501, "intelligence.engine_fault", ErrorType.Unexpected },
        { 502, "intelligence.unreachable", ErrorType.Unavailable },
        { 503, "intelligence.unreachable", ErrorType.Unavailable },
        { 504, "intelligence.unreachable", ErrorType.Unavailable },
    };

    [Theory]
    [MemberData(nameof(Statuses))]
    public void ClientResultException_MapsItsStatus(int status, string code, ErrorType type)
    {
        var error = Classify(ClientResult(status), out var matched);

        matched.Should().BeTrue();
        error!.Code.Should().Be(code);
        error.Type.Should().Be(type);
        error.Message.Should().NotContain("sentinel");
    }

    [Theory]
    [MemberData(nameof(Statuses))]
    public void HttpOperationException_MapsItsStatusCode_LikeTheConnectorRaisesIt(int status, string code, ErrorType type)
    {
        var exception = new HttpOperationException((HttpStatusCode)status, Sentinel, Sentinel, ClientResult(status));

        var error = Classify(exception, out var matched);

        matched.Should().BeTrue();
        error!.Code.Should().Be(code);
        error.Type.Should().Be(type);
        error.Message.Should().NotContain("sentinel");
        error.Details.Should().BeNullOrEmpty();
    }

    [Fact]
    public void AConnectorExceptionWithoutAStatus_IsATransportFault()
    {
        var transport = new ClientResultException(new FakePipelineResponse(0), new HttpRequestException(Sentinel));
        var wrapped = new HttpOperationException(Sentinel, transport);

        foreach (var exception in new Exception[] { transport, wrapped, new HttpRequestException(Sentinel) })
        {
            var error = Classify(exception, out var matched);

            matched.Should().BeTrue();
            error!.Code.Should().Be("intelligence.unreachable");
            error.Type.Should().Be(ErrorType.Unavailable);
            error.Message.Should().NotContain("sentinel");
        }
    }

    [Fact]
    public void ATimeout_WithTheCallersTokenLive_IsATimeout()
    {
        foreach (var exception in new Exception[] { new TaskCanceledException(Sentinel, new TimeoutException()), new TimeoutException(Sentinel) })
        {
            var error = Classify(exception, out var matched);

            matched.Should().BeTrue();
            error!.Code.Should().Be("intelligence.timeout");
            error.Type.Should().Be(ErrorType.Timeout);
        }
    }

    [Fact]
    public void ATransportFaultWrappingATimeout_IsATimeout()
    {
        var exception = new ClientResultException(new FakePipelineResponse(0), new TaskCanceledException(Sentinel, new TimeoutException()));

        var error = Classify(exception, out _);

        error!.Code.Should().Be("intelligence.timeout");
    }

    [Fact]
    public void ACancelledCallerToken_NeverBecomesAResult_ForAnOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Classify(new TaskCanceledException(), out var matched, cts.Token);

        matched.Should().BeFalse("the caller's own OperationCanceledException propagates untouched");
    }

    [Fact]
    public void ACancelledCallerToken_WithAWrappedCancellation_MatchesWithNoError_SoItSurfacesAsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var wrapped = new HttpOperationException(Sentinel, new TaskCanceledException());

        var error = Classify(wrapped, out var matched, cts.Token);

        matched.Should().BeTrue();
        error.Should().BeNull();
        var act = () => SemanticKernelFaults.Surface(wrapped, error, cts.Token, NullLogger.Instance, "complete", "gpt-test");
        act.Should().Throw<OperationCanceledException>().Which.CancellationToken.Should().Be(cts.Token);
    }

    [Fact]
    public void AJsonFault_IsAClassifiedEngineFault()
    {
        var error = Classify(new JsonException(Sentinel), out var matched);

        matched.Should().BeTrue();
        error!.Code.Should().Be("intelligence.engine_fault");
        error.Message.Should().NotContain("sentinel");
    }

    public static TheoryData<Exception> Defects => new()
    {
        new InvalidOperationException(Sentinel),
        new NullReferenceException(),
        new ArgumentException(Sentinel),
        new InvalidCastException(),
        new KernelException(Sentinel),
        new KeyNotFoundException(),
    };

    [Theory]
    [MemberData(nameof(Defects))]
    public void AnythingElse_IsNotClassified(Exception defect)
    {
        Classify(defect, out var matched).Should().BeNull();

        matched.Should().BeFalse();
    }

    [Theory]
    [InlineData(429, true)]
    [InlineData(500, true)]
    [InlineData(503, true)]
    [InlineData(408, false)]
    [InlineData(400, false)]
    [InlineData(401, false)]
    [InlineData(404, false)]
    public void OnlyARealHttp429Or5xx_IsTransient(int status, bool transient)
    {
        SemanticKernelFaults.IsTransientHttpFailure(ClientResult(status), CancellationToken.None).Should().Be(transient);
        SemanticKernelFaults.IsTransientHttpFailure(
            new HttpOperationException((HttpStatusCode)status, Sentinel, Sentinel, ClientResult(status)), CancellationToken.None).Should().Be(transient);
    }

    [Fact]
    public void ATransportFaultOrTimeout_IsNotTransient_BecauseItIsNotAnHttpStatus()
    {
        SemanticKernelFaults.IsTransientHttpFailure(new HttpRequestException(), CancellationToken.None).Should().BeFalse();
        SemanticKernelFaults.IsTransientHttpFailure(new TaskCanceledException(), CancellationToken.None).Should().BeFalse();
        SemanticKernelFaults.IsTransientHttpFailure(
            new ClientResultException(new FakePipelineResponse(0), new HttpRequestException()), CancellationToken.None).Should().BeFalse();
    }

    [Fact]
    public void ACancelledToken_IsNeverTransient()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        SemanticKernelFaults.IsTransientHttpFailure(ClientResult(429), cts.Token).Should().BeFalse();
    }

    /// <summary>
    /// Pins the SDK behaviour the registration overrides: with default options System.ClientModel 1.10.0 dispatches a
    /// throttled request four times. If this ever changes, the explicit no-retry policy can be revisited.
    /// </summary>
    [Fact]
    public async Task SdkDefault_RetriesAThrottledRequestThreeTimes_WhichTheRegistrationDisables()
    {
        var count = 0;
        var handler = new ScriptedHandler((n, _, _) =>
        {
            count = n;
            return SemanticKernelHarness.Status(429);
        });
        var options = new OpenAIClientOptions { Transport = new HttpClientPipelineTransport(new HttpClient(handler)) };
        var embeddings = new OpenAIClient(new ApiKeyCredential("k"), options).GetEmbeddingClient("emb-test");

        var act = () => embeddings.GenerateEmbeddingsAsync(["a"]);

        await act.Should().ThrowAsync<ClientResultException>();
        count.Should().Be(4);
    }
}
