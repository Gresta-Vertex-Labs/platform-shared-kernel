using System.Diagnostics;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTelemetry;
using OpenTelemetry.Logs;
using Microsoft.Extensions.Logging;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Security.Abstractions;
using SharedKernel.ServiceDefaults.Telemetry;
using SharedKernel.Testing.Security;

namespace SharedKernel.ServiceDefaults.Security.Tests;

/// <summary>
/// P-566: <c>UseSharedKernelRequestContext()</c> — the HTTP inbound adapter that absorbed
/// <c>SharedKernel.Presentation.WebApi</c>'s <c>CorrelationIdMiddleware</c> (its validation cases are kept here) and
/// now also opens the request's <see cref="RequestContextScope"/>.
/// </summary>
public sealed class RequestContextMiddlewareTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());

    private static (HttpContext Context, FiringHttpResponseFeature Response) CreateHttpContext(IUserContext? user = null)
    {
        var services = new ServiceCollection();
        services.AddScoped<IUserContext>(_ => user ?? new FakeUserContext { TenantId = Tenant });
        services.AddSharedKernelRequestContext();

        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider().CreateScope().ServiceProvider };
        var response = new FiringHttpResponseFeature();
        context.Features.Set<IHttpResponseFeature>(response);
        context.Features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(Stream.Null));
        return (context, response);
    }

    private static RequestContextMiddleware Create(RequestDelegate next) =>
        new(next, NullLogger<RequestContextMiddleware>.Instance);

    [Fact]
    public async Task WellFormedHeader_IsKept_EchoedAndMadeAmbient()
    {
        var (http, response) = CreateHttpContext();
        http.Request.Headers[WellKnownHeaders.CorrelationId] = "client-correlation-id";
        IRequestContext? seen = null;

        await Create(_ => { seen = RequestContextScope.Current; return Task.CompletedTask; }).InvokeAsync(http);
        await response.FireOnStartingAsync();

        seen!.CorrelationId.Should().Be("client-correlation-id");
        http.Response.Headers[WellKnownHeaders.CorrelationId].ToString().Should().Be("client-correlation-id");
        RequestContextScope.Current.Should().BeNull("the scope ends with the request");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("has spaces")]
    [InlineData("has/slash")]
    [InlineData("has<angle>brackets")]
    [InlineData("has\"quote")]
    public async Task MissingOrInvalidHeader_IsReplacedByANewId(string? supplied)
    {
        var (http, response) = CreateHttpContext();
        if (supplied is not null)
            http.Request.Headers[WellKnownHeaders.CorrelationId] = supplied;
        string? seen = null;

        await Create(_ => { seen = RequestContextScope.Current?.CorrelationId; return Task.CompletedTask; }).InvokeAsync(http);
        await response.FireOnStartingAsync();

        seen.Should().NotBe(supplied);
        Guid.TryParseExact(seen, "D", out _).Should().BeTrue();
        http.Response.Headers[WellKnownHeaders.CorrelationId].ToString().Should().Be(seen);
    }

    [Fact]
    public async Task OverlongHeader_IsReplaced()
    {
        var (http, _) = CreateHttpContext();
        var overLength = new string('a', CorrelationIds.MaxLength + 1);
        http.Request.Headers[WellKnownHeaders.CorrelationId] = overLength;
        string? seen = null;

        await Create(_ => { seen = RequestContextScope.Current?.CorrelationId; return Task.CompletedTask; }).InvokeAsync(http);

        seen.Should().NotBe(overLength);
    }

    [Fact]
    public async Task RejectedValue_NeverReachesBaggageOrTheResponse()
    {
        using var activity = new Activity("request").Start();
        var (http, response) = CreateHttpContext();
        const string rejected = "invalid value with spaces";
        http.Request.Headers[WellKnownHeaders.CorrelationId] = rejected;

        await Create(_ => Task.CompletedTask).InvokeAsync(http);
        await response.FireOnStartingAsync();

        activity.Baggage.Should().NotContain(kv => kv.Value == rejected);
        activity.GetBaggageItem(WellKnownBaggageKeys.CorrelationId).Should().NotBeNullOrEmpty();
        http.Response.Headers[WellKnownHeaders.CorrelationId].ToString().Should().NotBe(rejected);
    }

    [Fact]
    public async Task ResponseHeader_IsSet_EvenWhenTheRestOfThePipelineShortCircuits()
    {
        var (http, response) = CreateHttpContext();

        await Create(ctx => { ctx.Response.StatusCode = StatusCodes.Status500InternalServerError; return Task.CompletedTask; })
            .InvokeAsync(http);
        await response.FireOnStartingAsync();

        http.Response.Headers.ContainsKey(WellKnownHeaders.CorrelationId).Should().BeTrue();
    }

    [Fact]
    public async Task AmbientContext_CarriesTheCallersIdentityAndTenant()
    {
        var (http, _) = CreateHttpContext(new FakeUserContext { SubjectId = "u-1", ClientId = "spa", TenantId = Tenant });
        IRequestContext? seen = null;

        await Create(_ => { seen = RequestContextScope.Current; return Task.CompletedTask; }).InvokeAsync(http);

        seen!.UserId.Should().Be("u-1");
        seen.TenantId.Should().Be(Tenant);
        seen.ClientId.Should().Be("spa");
        seen.ActorKind.Should().Be(ActorKind.User);
    }

    /// <summary>
    /// The caller is read lazily, so the middleware can run before <c>UseAuthentication()</c>: nothing resolves the
    /// scoped <see cref="IUserContext"/> until the request asks who is calling.
    /// </summary>
    [Fact]
    public async Task Caller_IsNotResolvedUntilSomethingAsks()
    {
        var resolutions = 0;
        var services = new ServiceCollection();
        services.AddScoped<IUserContext>(_ => { resolutions++; return new FakeUserContext(); });
        services.AddSharedKernelRequestContext();
        var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider().CreateScope().ServiceProvider };
        http.Features.Set<IHttpResponseFeature>(new FiringHttpResponseFeature());

        await Create(ctx =>
        {
            resolutions.Should().Be(0, "entering the scope must not resolve the caller");
            _ = RequestContextScope.Current!.UserId;
            return Task.CompletedTask;
        }).InvokeAsync(http);

        resolutions.Should().Be(1);
    }

    /// <summary>
    /// The DI-registered <see cref="IRequestContext"/> resolves to the ambient one inside the request, so handlers,
    /// repositories and the accessor all see the same caller and correlation id.
    /// </summary>
    [Fact]
    public async Task RegisteredRequestContext_IsTheAmbientOneInsideTheRequest()
    {
        var (http, _) = CreateHttpContext();
        http.Request.Headers[WellKnownHeaders.CorrelationId] = "abc-123";
        string? fromDi = null;

        await Create(ctx =>
        {
            fromDi = ctx.RequestServices.GetRequiredService<IRequestContext>().CorrelationId;
            return Task.CompletedTask;
        }).InvokeAsync(http);

        fromDi.Should().Be("abc-123");
    }

    [Fact]
    public void UseSharedKernelRequestContext_WithoutAddSharedKernelRequestContext_Throws()
    {
        var app = new ApplicationBuilder(new ServiceCollection().BuildServiceProvider());

        var act = () => app.UseSharedKernelRequestContext();

        act.Should().Throw<InvalidOperationException>().WithMessage("*AddSharedKernelRequestContext*");
    }

    /// <summary>
    /// The correlation id reaches every log record of the request through the real
    /// <see cref="BaggageLogRecordProcessor"/> of <c>SharedKernel.ServiceDefaults</c>.
    /// </summary>
    [Fact]
    public async Task CorrelationId_ReachesLogRecordsThroughTheBaggageProcessor()
    {
        var captured = new List<IReadOnlyList<KeyValuePair<string, object?>>?>();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddOpenTelemetry(options =>
        {
            options.AddProcessor(new BaggageLogRecordProcessor());
            options.AddProcessor(new CapturingProcessor(captured));
        }));
        var logger = loggerFactory.CreateLogger("request");
        using var activity = new Activity("request").Start();
        var (http, _) = CreateHttpContext();
        http.Request.Headers[WellKnownHeaders.CorrelationId] = "log-correlation-1";

        await Create(_ => { logger.LogInformation("Handled."); return Task.CompletedTask; }).InvokeAsync(http);

        captured.Should().ContainSingle();
        captured[0]!.Should().Contain(kv =>
            kv.Key == WellKnownBaggageKeys.CorrelationId && Equals(kv.Value, "log-correlation-1"));
    }

    private sealed class CapturingProcessor(List<IReadOnlyList<KeyValuePair<string, object?>>?> sink) : BaseProcessor<LogRecord>
    {
        public override void OnEnd(LogRecord data) => sink.Add(data.Attributes is null ? null : [.. data.Attributes]);
    }

    /// <summary>
    /// <see cref="DefaultHttpContext"/>'s response feature records <c>OnStarting</c> callbacks but never fires them
    /// outside a real server; this one lets a test fire them.
    /// </summary>
    private sealed class FiringHttpResponseFeature : HttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _onStarting = [];

        public override void OnStarting(Func<object, Task> callback, object state) => _onStarting.Add((callback, state));

        public async Task FireOnStartingAsync()
        {
            foreach (var (callback, state) in _onStarting)
                await callback(state);
        }
    }
}
