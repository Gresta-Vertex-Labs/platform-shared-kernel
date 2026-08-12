using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Communication.Grpc.Interceptors;

namespace SharedKernel.Communication.Grpc.Tests.Interceptors;

public sealed class CorrelationTracingInterceptorTests
{
    private static readonly Method<string, string> TestMethod = new(
        MethodType.Unary,
        "TestService",
        "TestMethod",
        Marshallers.StringMarshaller,
        Marshallers.StringMarshaller);

    private static CorrelationTracingInterceptor CreateInterceptor() =>
        new(NullLogger<CorrelationTracingInterceptor>.Instance);

    private static ClientInterceptorContext<string, string> BuildContext(Metadata? headers)
    {
        var callOptions = headers is not null
            ? new CallOptions(headers)
            : new CallOptions();
        return new ClientInterceptorContext<string, string>(TestMethod, "localhost", callOptions);
    }

    private static Metadata.Entry? GetEntry(Metadata metadata, string key) =>
        metadata.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void AsyncUnaryCall_WithNoActivity_InjectsGuidCorrelationId()
    {
        // Arrange
        Activity.Current = null;
        var interceptor = CreateInterceptor();
        Metadata? capturedMetadata = null;
        var context = BuildContext(null);

        // Act
        interceptor.AsyncUnaryCall("request", context,
            (_, ctx) =>
            {
                capturedMetadata = ctx.Options.Headers;
                return MakeFakeUnaryCall();
            });

        // Assert
        capturedMetadata.Should().NotBeNull();
        var entry = GetEntry(capturedMetadata!, CorrelationTracingInterceptor.CorrelationIdKey);
        entry.Should().NotBeNull("x-correlation-id must be injected");
    }

    [Fact]
    public void AsyncUnaryCall_WithActiveActivity_InjectsTraceParentAndCorrelationId()
    {
        // Arrange
        var interceptor = CreateInterceptor();
        Metadata? capturedMetadata = null;
        var context = BuildContext(null);

        using var activity = new Activity("test-operation");
        activity.Start();
        try
        {
            // Act
            interceptor.AsyncUnaryCall("request", context,
                (_, ctx) =>
                {
                    capturedMetadata = ctx.Options.Headers;
                    return MakeFakeUnaryCall();
                });
        }
        finally
        {
            activity.Stop();
        }

        // Assert
        capturedMetadata.Should().NotBeNull();
        GetEntry(capturedMetadata!, CorrelationTracingInterceptor.TraceParentKey)
            .Should().NotBeNull("traceparent must be injected when Activity is active");
        GetEntry(capturedMetadata!, CorrelationTracingInterceptor.CorrelationIdKey)
            .Should().NotBeNull("x-correlation-id must be injected");
    }

    [Fact]
    public void AsyncUnaryCall_WhenCallerAlreadySetCorrelationId_DoesNotOverwrite()
    {
        // Arrange
        const string callerValue = "caller-set-correlation-id";
        var interceptor = CreateInterceptor();
        Metadata? capturedMetadata = null;

        var existingHeaders = new Metadata { { CorrelationTracingInterceptor.CorrelationIdKey, callerValue } };
        var context = BuildContext(existingHeaders);

        // Act
        interceptor.AsyncUnaryCall("request", context,
            (_, ctx) =>
            {
                capturedMetadata = ctx.Options.Headers;
                return MakeFakeUnaryCall();
            });

        // Assert
        capturedMetadata.Should().NotBeNull();
        GetEntry(capturedMetadata!, CorrelationTracingInterceptor.CorrelationIdKey)!.Value
            .Should().Be(callerValue, "interceptor must not overwrite caller-supplied x-correlation-id");
    }

    [Fact]
    public void AsyncUnaryCall_NeverPropagatesException()
    {
        // Arrange — interceptor body is safe; test that continuation is always reached
        var interceptor = CreateInterceptor();
        var context = BuildContext(null);
        var continuationCalled = false;

        // Act
        Action act = () => interceptor.AsyncUnaryCall("request", context,
            (_, _) =>
            {
                continuationCalled = true;
                return MakeFakeUnaryCall();
            });

        // Assert
        act.Should().NotThrow("interceptor exceptions must be swallowed, not propagated");
        continuationCalled.Should().BeTrue();
    }

    [Fact]
    public void AsyncServerStreamingCall_InjectsCorrelationId()
    {
        // Arrange
        Activity.Current = null;
        var interceptor = CreateInterceptor();
        Metadata? capturedMetadata = null;
        var context = BuildContext(null);

        // Act
        interceptor.AsyncServerStreamingCall("request", context,
            (_, ctx) =>
            {
                capturedMetadata = ctx.Options.Headers;
                return new AsyncServerStreamingCall<string>(
                    Substitute.For<IAsyncStreamReader<string>>(),
                    Task.FromResult(new Metadata()),
                    () => Status.DefaultSuccess,
                    () => new Metadata(),
                    () => { });
            });

        // Assert
        GetEntry(capturedMetadata!, CorrelationTracingInterceptor.CorrelationIdKey)
            .Should().NotBeNull("x-correlation-id must be injected on server-streaming calls");
    }

    [Fact]
    public void AsyncClientStreamingCall_InjectsCorrelationId()
    {
        // Arrange
        Activity.Current = null;
        var interceptor = CreateInterceptor();
        Metadata? capturedMetadata = null;
        var context = BuildContext(null);

        // Act
        interceptor.AsyncClientStreamingCall(context,
            ctx =>
            {
                capturedMetadata = ctx.Options.Headers;
                return new AsyncClientStreamingCall<string, string>(
                    Substitute.For<IClientStreamWriter<string>>(),
                    Task.FromResult("response"),
                    Task.FromResult(new Metadata()),
                    () => Status.DefaultSuccess,
                    () => new Metadata(),
                    () => { });
            });

        // Assert
        GetEntry(capturedMetadata!, CorrelationTracingInterceptor.CorrelationIdKey)
            .Should().NotBeNull("x-correlation-id must be injected on client-streaming calls");
    }

    [Fact]
    public void AsyncDuplexStreamingCall_InjectsCorrelationId()
    {
        // Arrange
        Activity.Current = null;
        var interceptor = CreateInterceptor();
        Metadata? capturedMetadata = null;
        var context = BuildContext(null);

        // Act
        interceptor.AsyncDuplexStreamingCall(context,
            ctx =>
            {
                capturedMetadata = ctx.Options.Headers;
                return new AsyncDuplexStreamingCall<string, string>(
                    Substitute.For<IClientStreamWriter<string>>(),
                    Substitute.For<IAsyncStreamReader<string>>(),
                    Task.FromResult(new Metadata()),
                    () => Status.DefaultSuccess,
                    () => new Metadata(),
                    () => { });
            });

        // Assert
        GetEntry(capturedMetadata!, CorrelationTracingInterceptor.CorrelationIdKey)
            .Should().NotBeNull("x-correlation-id must be injected on duplex-streaming calls");
    }

    /// <summary>
    /// T-31 (P-356/WO-056): the GUID fallback must be the canonical hyphenated <c>"D"</c> format —
    /// never <c>Guid.NewGuid().ToString("N")</c> (the confirmed SK0011 violation). A bare
    /// <c>Guid.TryParse</c> accepts both formats and would not catch a regression back to
    /// <c>"N"</c>, so this asserts the exact format via <see cref="Guid.TryParseExact"/> and
    /// hyphen presence explicitly.
    /// </summary>
    [Fact]
    public void AsyncUnaryCall_WithNoActivity_FallbackIsCanonicalHyphenatedFormat()
    {
        // Arrange
        Activity.Current = null;
        var interceptor = CreateInterceptor();
        Metadata? capturedMetadata = null;
        var context = BuildContext(null);

        // Act
        interceptor.AsyncUnaryCall("request", context,
            (_, ctx) =>
            {
                capturedMetadata = ctx.Options.Headers;
                return MakeFakeUnaryCall();
            });

        // Assert
        capturedMetadata.Should().NotBeNull();
        var entry = GetEntry(capturedMetadata!, CorrelationTracingInterceptor.CorrelationIdKey);
        entry.Should().NotBeNull();
        var value = entry!.Value;
        Guid.TryParseExact(value, "D", out _).Should().BeTrue(
            "the fallback must be the canonical hyphenated GUID format (\"D\"), not Guid.NewGuid().ToString(\"N\")");
        value.Should().Contain("-");
        value.Should().HaveLength(36);
    }

    private static AsyncUnaryCall<string> MakeFakeUnaryCall() =>
        new(
            Task.FromResult("response"),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });
}
