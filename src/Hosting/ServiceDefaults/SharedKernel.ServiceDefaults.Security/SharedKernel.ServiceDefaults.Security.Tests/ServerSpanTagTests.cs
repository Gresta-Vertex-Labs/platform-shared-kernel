using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using SharedKernel.MultiTenancy.Extensions;
using SharedKernel.MultiTenancy.Middleware;
using SharedKernel.MultiTenancy.Resolution;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Security.Abstractions;
using SharedKernel.ServiceDefaults.Security;
using SharedKernel.Testing.Logging;
using SharedKernel.Testing.Security;
using SharedKernel.Testing.Telemetry;

namespace SharedKernel.ServiceDefaults.Security.Tests;

/// <summary>Tests that listen to the process-wide <c>Microsoft.AspNetCore</c> activity source run one at a time.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class NonParallelActivityCollection
{
    /// <summary>The collection name.</summary>
    public const string Name = "NonParallelActivity";
}

/// <summary>
/// The ASP.NET Core server span carries the platform's ids: <c>correlation.id</c> from the request-context middleware
/// and <c>tenant.id</c> from tenant resolution (or the credential's asserted tenant without it), written before
/// <c>UseExceptionHandler()</c>. The span exists only while something listens to the <c>Microsoft.AspNetCore</c>
/// source, so each test registers a listener and isolates its span by trace id.
/// </summary>
[Collection(NonParallelActivityCollection.Name)]
public sealed class ServerSpanTagTests : IDisposable
{
    private const string ServerSpanName = "Microsoft.AspNetCore.Hosting.HttpRequestIn";
    private static readonly TenantId Asserted = new(Guid.NewGuid());
    private static readonly TenantId FromHeader = new(Guid.NewGuid());

    private readonly ActivityRecorder _recorder = ActivityRecorder.StartRecording("Microsoft.AspNetCore");

    public void Dispose() => _recorder.Dispose();

    // ---- correlation.id ---------------------------------------------------------------------------------

    [Fact]
    public async Task ValidInboundCorrelationId_IsTheSpansCorrelationId()
    {
        await using var host = await TestHost.StartAsync(new FakeUserContext { TenantId = Asserted });

        var (response, span) = await Send(host, request =>
            request.Headers.Add(WellKnownHeaders.CorrelationId, "client-correlation-id"));

        response.Headers.GetValues(WellKnownHeaders.CorrelationId).Should().ContainSingle().Which.Should().Be("client-correlation-id");
        span.ShouldHaveTag(WellKnownTagKeys.CorrelationId, "client-correlation-id");
    }

    [Fact]
    public async Task MissingInboundCorrelationId_TagsTheGeneratedIdThatIsEchoed()
    {
        await using var host = await TestHost.StartAsync(new FakeUserContext { TenantId = Asserted });

        var (response, span) = await Send(host);

        var echoed = response.Headers.GetValues(WellKnownHeaders.CorrelationId).Single();
        Guid.TryParseExact(echoed, "D", out _).Should().BeTrue();
        span.ShouldHaveTag(WellKnownTagKeys.CorrelationId, echoed);
    }

    [Fact]
    public async Task InvalidInboundCorrelationId_IsReplaced_AndTheRejectedValueNeverReachesTheSpan()
    {
        await using var host = await TestHost.StartAsync(new FakeUserContext { TenantId = Asserted });
        const string rejected = "rejected value with spaces";

        var (response, span) = await Send(host, request =>
            request.Headers.TryAddWithoutValidation(WellKnownHeaders.CorrelationId, rejected));

        var echoed = response.Headers.GetValues(WellKnownHeaders.CorrelationId).Single();
        echoed.Should().NotBe(rejected);
        span.ShouldHaveTag(WellKnownTagKeys.CorrelationId, echoed).ShouldNotLeak(rejected);
    }

    // ---- tenant.id --------------------------------------------------------------------------------------

    [Fact]
    public async Task WithoutMultiTenancy_TheCredentialsTenantIsTagged()
    {
        await using var host = await TestHost.StartAsync(new FakeUserContext { TenantId = Asserted });

        var (_, span) = await Send(host);

        span.ShouldHaveTag(WellKnownTagKeys.TenantId, Asserted.ToString());
    }

    [Fact]
    public async Task WithMultiTenancy_TheResolvedTenantIsTagged_NotTheAssertedOne()
    {
        await using var host = await TestHost.StartAsync(new FakeUserContext { TenantId = Asserted }, multiTenancy: true);

        var (_, span) = await Send(host, request =>
            request.Headers.Add(WellKnownHeaders.TenantId, FromHeader.ToString()));

        span.ShouldHaveTag(WellKnownTagKeys.TenantId, FromHeader.ToString());
    }

    [Fact]
    public async Task WithMultiTenancy_AHeaderResolvedTenantWithNoCredentialTenantIsTagged()
    {
        await using var host = await TestHost.StartAsync(new FakeUserContext { TenantId = null }, multiTenancy: true);

        var (_, span) = await Send(host, request =>
            request.Headers.Add(WellKnownHeaders.TenantId, FromHeader.ToString()));

        span.ShouldHaveTag(WellKnownTagKeys.TenantId, FromHeader.ToString());
    }

    [Fact]
    public async Task InactiveTenant_SpanKeepsTheAssertedTenant_WhileTheRequestRunsWithNone()
    {
        await using var host = await TestHost.StartAsync(
            new FakeUserContext { TenantId = Asserted },
            multiTenancy: true,
            configure: services => services.AddSingleton<ITenantStatusValidator>(new InactiveTenants()));

        var (response, span) = await Send(host, request =>
            request.Headers.Add(WellKnownHeaders.TenantId, FromHeader.ToString()));

        span.ShouldHaveTag(WellKnownTagKeys.TenantId, Asserted.ToString());
        (await Observed(response)).TenantId.Should().BeNull("an inactive tenant fails closed (rule 15)");
    }

    [Fact]
    public async Task AnonymousRequestWithNoTenant_HasNoTenantTag()
    {
        await using var host = await TestHost.StartAsync(AnonymousUserContext.Instance);

        var (_, span) = await Send(host);

        span.ShouldNotHaveTag(WellKnownTagKeys.TenantId);
    }

    [Fact]
    public async Task ForgedTenantBaggageAndHeader_NeverReachTheTagOrTheContext()
    {
        await using var host = await TestHost.StartAsync(AnonymousUserContext.Instance);
        var forged = Guid.NewGuid().ToString();

        var (response, span) = await Send(host, request =>
        {
            request.Headers.Add("baggage", $"{WellKnownBaggageKeys.TenantId}={forged}");
            request.Headers.Add(WellKnownHeaders.TenantId, forged);
        });

        span.ShouldNotHaveTag(WellKnownTagKeys.TenantId).ShouldNotLeak(forged);
        (await Observed(response)).TenantId.Should().BeNull();
    }

    // ---- a handled exception ----------------------------------------------------------------------------

    [Fact]
    public async Task AHandledException_KeepsTheTagsAndTheEchoedCorrelationId()
    {
        await using var host = await TestHost.StartAsync(
            new FakeUserContext { TenantId = Asserted },
            multiTenancy: true,
            webApi: true);

        var (response, span) = await Send(host, path: "/boom", configure: request =>
        {
            request.Headers.Add(WellKnownHeaders.CorrelationId, "boom-correlation");
            request.Headers.Add(WellKnownHeaders.TenantId, FromHeader.ToString());
        });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Headers.GetValues(WellKnownHeaders.CorrelationId).Should().ContainSingle().Which.Should().Be("boom-correlation");
        span.ShouldHaveTag(WellKnownTagKeys.CorrelationId, "boom-correlation")
            .ShouldHaveTag(WellKnownTagKeys.TenantId, FromHeader.ToString());
    }

    // ---- forged identity headers ------------------------------------------------------------------------

    [Fact]
    public async Task ForgedIdentityHeaders_ChangeNothing_ForACredentialedCaller()
    {
        var logs = new InMemoryLoggerFactory();
        await using var host = await TestHost.StartAsync(
            new FakeUserContext { SubjectId = "real-user", ClientId = "real-client", TenantId = Asserted },
            logs: logs);

        var (response, span) = await Send(host, request =>
        {
            request.Headers.Add(WellKnownHeaders.ActorId, "forged-actor-7f3a");
            request.Headers.Add(WellKnownHeaders.ActorKind, nameof(ActorKind.System));
            request.Headers.Add(WellKnownHeaders.ClientId, "forged-client-7f3a");
            request.Headers.Add(WellKnownHeaders.TenantId, Guid.NewGuid().ToString());
        });

        var seen = await Observed(response);
        seen.UserId.Should().Be("real-user");
        seen.ClientId.Should().Be("real-client");
        seen.ActorKind.Should().Be(nameof(ActorKind.User));
        seen.TenantId.Should().Be(Asserted.ToString());
        span.ShouldNotLeak("forged-actor-7f3a", "forged-client-7f3a");
        AllLogs(logs).ShouldNotLeak("forged-actor-7f3a", "forged-client-7f3a");
    }

    [Fact]
    public async Task ForgedSystemActor_ForAnAnonymousCaller_StaysAnonymous()
    {
        await using var host = await TestHost.StartAsync(AnonymousUserContext.Instance);

        var (response, _) = await Send(host, request =>
        {
            request.Headers.Add(WellKnownHeaders.ActorId, "forged-actor-7f3a");
            request.Headers.Add(WellKnownHeaders.ActorKind, nameof(ActorKind.System));
        });

        var seen = await Observed(response);
        seen.ActorKind.Should().Be(nameof(ActorKind.Anonymous));
        seen.UserId.Should().BeNull();
    }

    [Theory]
    [InlineData(WellKnownHeaders.ActorKind, "7")]
    [InlineData(WellKnownHeaders.TenantId, "not-a-tenant")]
    [InlineData(WellKnownHeaders.ActorId, "")]
    public async Task MalformedIdentityHeaders_DoNotThrow(string header, string value)
    {
        await using var host = await TestHost.StartAsync(new FakeUserContext { TenantId = Asserted });

        var (response, _) = await Send(host, request => request.Headers.TryAddWithoutValidation(header, value));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Observed(response)).TenantId.Should().Be(Asserted.ToString());
    }

    // ---- helpers ----------------------------------------------------------------------------------------

    private async Task<(HttpResponseMessage Response, Activity Span)> Send(
        WebApplication host,
        Action<HttpRequestMessage>? configure = null,
        string path = "/ctx")
    {
        var traceId = ActivityTraceId.CreateRandom();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("traceparent", $"00-{traceId.ToHexString()}-b7ad6b7169203331-01");
        configure?.Invoke(request);

        var response = await host.GetTestClient().SendAsync(request);
        return (response, await ServerSpanAsync(traceId));
    }

    private async Task<Activity> ServerSpanAsync(ActivityTraceId traceId)
    {
        // The span stops after the response has been handed back, so give it a moment.
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var span = _recorder.ForTrace(traceId).FirstOrDefault(a => a.OperationName == ServerSpanName);
            if (span is not null)
                return span;

            await Task.Delay(25);
        }

        throw new TimeoutException("The ASP.NET Core server span was not recorded.");
    }

    private static async Task<ObservedContext> Observed(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ObservedContext>())!;

    private static IReadOnlyList<SharedKernel.Testing.Logging.LogRecord> AllLogs(InMemoryLoggerFactory factory) =>
        [.. factory.Loggers.Values.SelectMany(logger => logger.Records)];

    private sealed record ObservedContext(string? UserId, string? ClientId, string ActorKind, string? TenantId, string? CorrelationId);

    private sealed class InactiveTenants : ITenantStatusValidator
    {
        public Task<bool> IsActiveAsync(TenantId tenantId, CancellationToken ct) => Task.FromResult(false);
    }

    private static class TestHost
    {
        public static async Task<WebApplication> StartAsync(
            IUserContext user,
            bool multiTenancy = false,
            bool webApi = false,
            Action<IServiceCollection>? configure = null,
            InMemoryLoggerFactory? logs = null)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
            builder.WebHost.UseTestServer();
            
            builder.Services.AddScoped<IUserContext>(_ => user);
            builder.Services.AddSharedKernelRequestContext();
            if (logs is not null)
                builder.Services.AddSingleton<ILoggerFactory>(logs);

            if (multiTenancy)
            {
                builder.Services.AddSharedKernelMultiTenancy(o => o.StrategyOrder = [TenantResolutionStrategyNames.Header]);
            }

            if (webApi)
                builder.AddSharedKernelWebApi();

            configure?.Invoke(builder.Services);

            var app = builder.Build();
            app.UseSharedKernelRequestContext();
            if (webApi)
                app.UseSharedKernelWebApi(p => p.BeforeAuthorization(a => a.UseMiddleware<TenantResolutionMiddleware>()));
            else if (multiTenancy)
                app.UseMiddleware<TenantResolutionMiddleware>();

            app.MapGet("/ctx", () =>
            {
                var context = RequestContextScope.Current;
                return Results.Json(new ObservedContext(
                    context?.UserId,
                    context?.ClientId,
                    context?.ActorKind.ToString() ?? string.Empty,
                    context?.TenantId?.ToString(),
                    context?.CorrelationId));
            });
            app.MapGet("/boom", string () => throw new InvalidOperationException("boom"));

            await app.StartAsync();
            return app;
        }
    }
}
