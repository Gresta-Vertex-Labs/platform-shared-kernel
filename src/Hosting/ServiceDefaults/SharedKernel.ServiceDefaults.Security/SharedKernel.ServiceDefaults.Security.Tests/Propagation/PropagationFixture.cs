using SharedKernel.Application.Pipeline;
using SharedKernel.Application.Mediator.MediatR;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using Grpc.Core;
using MassTransit;
using SharedKernel.Application.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using SharedKernel.Communication;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Messaging.Abstractions.MessageBus;
using SharedKernel.Messaging.MassTransit.Context;
using SharedKernel.Messaging.MassTransit.Extensions;
using SharedKernel.Messaging.MassTransit.MessageBus;
using SharedKernel.Presentation.Grpc;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Results;
using SharedKernel.Scheduling.Extensions;
using SharedKernel.Scheduling.Policies;
using SharedKernel.Security.Abstractions;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Security;

namespace SharedKernel.ServiceDefaults.Security.Tests.Propagation;

/// <summary>What one inbound hop at the downstream service received.</summary>
internal sealed record ReceivedCall(
    string Hop,
    string? CorrelationHeader,
    string? AmbientCorrelationId,
    string? TenantHeader,
    string? ActorKindHeader,
    string? ActorIdHeader,
    string? IdempotencyKey);

/// <summary>Collects every call the downstream service received, keyed by the hop that sent it.</summary>
internal sealed class CallRecorder
{
    private readonly ConcurrentQueue<ReceivedCall> _calls = new();

    public void Add(ReceivedCall call) => _calls.Enqueue(call);

    public async Task<ReceivedCall> WaitForAsync(string hop, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        while (DateTime.UtcNow < deadline)
        {
            var call = _calls.FirstOrDefault(c => c.Hop == hop);
            if (call is not null)
                return call;

            await Task.Delay(25);
        }

        throw new TimeoutException($"The downstream service received no '{hop}' call.");
    }
}

/// <summary>
/// The downstream service: a real ASP.NET Core host on <see cref="TestServer"/> that runs the platform's own inbound
/// adapter, <c>UseSharedKernelRequestContext()</c>, for HTTP and gRPC alike (gRPC runs through the HTTP pipeline), and records
/// what each call carried, including the correlation id the adapter made ambient.
/// </summary>
internal sealed class DownstreamService : IAsyncDisposable
{
    private readonly WebApplication _app;

    private DownstreamService(WebApplication app, CallRecorder recorder)
    {
        _app = app;
        Recorder = recorder;
    }

    public CallRecorder Recorder { get; }

    public static async Task<DownstreamService> StartAsync()
    {
        var recorder = new CallRecorder();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddSingleton(recorder);
        builder.Services.AddScoped<IUserContext>(_ => AnonymousUserContext.Instance);
        builder.Services.AddSharedKernelRequestContext();
        builder.AddSharedKernelGrpc();

        var app = builder.Build();
        app.UseSharedKernelRequestContext();
        app.UseRouting();

        app.MapPost("/record/{hop}", (string hop, HttpContext context, CallRecorder calls) =>
        {
            var idempotencyKey = context.GetIdempotencyKey();
            calls.Add(new ReceivedCall(
                hop,
                Header(context, WellKnownHeaders.CorrelationId),
                RequestContextScope.Current?.CorrelationId,
                Header(context, WellKnownHeaders.TenantId),
                Header(context, WellKnownHeaders.ActorKind),
                Header(context, WellKnownHeaders.ActorId),
                idempotencyKey));
            return Results.Ok();
        });
        app.MapGrpcService<PropagationProbeService>();

        await app.StartAsync();
        return new DownstreamService(app, recorder);
    }

    public HttpMessageHandler CreateHandler() => _app.GetTestServer().CreateHandler();

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    private static string? Header(HttpContext context, string name)
        => context.Request.Headers.TryGetValue(name, out var value) ? value.ToString() : null;
}

/// <summary>The downstream gRPC method: records the call's metadata and its ambient request context.</summary>
internal sealed class PropagationProbeService(CallRecorder recorder) : PropagationProbe.PropagationProbeBase
{
    public override Task<CaptureReply> Capture(CaptureRequest request, ServerCallContext context)
    {
        var ambient = RequestContextScope.Current;
        recorder.Add(new ReceivedCall(
            request.Hop,
            context.RequestHeaders.GetValue(WellKnownHeaders.CorrelationId),
            ambient?.CorrelationId,
            context.RequestHeaders.GetValue(WellKnownHeaders.TenantId),
            context.RequestHeaders.GetValue(WellKnownHeaders.ActorKind),
            context.RequestHeaders.GetValue(WellKnownHeaders.ActorId),
            IdempotencyKey: null));

        return Task.FromResult(new CaptureReply
        {
            CorrelationId = ambient?.CorrelationId ?? string.Empty,
            TenantId = ambient?.TenantId?.ToString() ?? string.Empty,
            ActorKind = ambient?.ActorKind.ToString() ?? string.Empty,
        });
    }
}

/// <summary>The typed REST client every upstream hop uses to call the downstream service.</summary>
public sealed class DownstreamRestClient(HttpClient httpClient)
{
    public async Task RecordAsync(string hop, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsync($"/record/{hop}", content: null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}

/// <summary>The message the front service publishes; its consumer calls the downstream service over REST.</summary>
public sealed record PropagationMessage(string Hop);

/// <summary>Consumes <see cref="PropagationMessage"/> and makes an outbound REST call from inside the consume.</summary>
internal sealed class PropagationConsumer(DownstreamRestClient client) : IConsumer<PropagationMessage>
{
    public Task Consume(ConsumeContext<PropagationMessage> context) => client.RecordAsync(context.Message.Hop);
}

/// <summary>The scheduled job's command: calls the downstream service over REST.</summary>
public sealed record CallDownstreamCommand : ICommand;

/// <summary>Handles <see cref="CallDownstreamCommand"/> by calling the downstream service.</summary>
public sealed class CallDownstreamCommandHandler(DownstreamRestClient client) : IRequestHandler<CallDownstreamCommand, Result>
{
    public async Task<Result> Handle(CallDownstreamCommand request, CancellationToken cancellationToken)
    {
        await client.RecordAsync("job", cancellationToken);
        return Result.Success();
    }
}

/// <summary>Sets the response version to the request's, which gRPC over <see cref="TestServer"/> needs.</summary>
internal sealed class ResponseVersionHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        response.Version = request.Version;
        return response;
    }
}

/// <summary>
/// The front service: an authenticated, tenanted HTTP API whose endpoints call the downstream service over REST,
/// over gRPC, and through the message bus (a consumer in the same service then calls downstream over REST).
/// </summary>
internal sealed class FrontService : IAsyncDisposable
{
    public const string UserId = "user-e2e";

    private readonly WebApplication _app;

    private FrontService(WebApplication app) => _app = app;

    public HttpClient CreateClient() => _app.GetTestServer().CreateClient();

    public static async Task<FrontService> StartAsync(DownstreamService downstream, TenantId tenant)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var services = builder.Services;

        // The caller: a signed-in user of one tenant (an authentication scheme would produce this IUserContext).
        services.AddScoped<IUserContext>(_ => new FakeUserContext { SubjectId = UserId, TenantId = tenant });
        services.AddSharedKernelRequestContext();

        AddDownstreamRestClient(services, builder.Configuration, downstream);

        services.AddSharedKernelCommunication(builder.Configuration).AddGrpcClient<PropagationProbe.PropagationProbeClient>(
            "downstream-grpc",
            client => client
                .Configure(o => o.Address = new Uri("http://downstream"))
                .HttpClientBuilder.ConfigurePrimaryHttpMessageHandler(() => new ResponseVersionHandler { InnerHandler = downstream.CreateHandler() }));

        // The bus, on MassTransit's in-memory transport, with the platform's caller propagation both ways.
        services.AddSharedKernelMessaging(o => o.ServiceName = "front-service").WithInboundRequestContext();
        services.AddMassTransitTestHarness(cfg =>
        {
            cfg.AddConsumer<PropagationConsumer>();
            cfg.UsingInMemory((context, bus) =>
            {
                bus.UseConsumeFilter(typeof(InboundRequestContextFilter<>), context);
                bus.ConfigureEndpoints(context);
            });
        });
        services.AddSingleton<IReadOnlyDictionary<Type, string>>(new ReadOnlyDictionary<Type, string>(new Dictionary<Type, string>()));
        services.AddScoped<ConventionSendEndpointResolver>();
        services.AddScoped<IMessageBus, MassTransitMessageBus>();

        var app = builder.Build();
        app.UseSharedKernelRequestContext();
        app.UseRouting();

        app.MapGet("/rest", async (DownstreamRestClient client) =>
        {
            await client.RecordAsync("http-rest");
            return Results.Ok();
        });
        app.MapGet("/grpc", async (PropagationProbe.PropagationProbeClient client) =>
        {
            var reply = await client.CaptureAsync(new CaptureRequest { Hop = "http-grpc" });
            return Results.Ok(reply.CorrelationId);
        });
        app.MapGet("/bus", async (IMessageBus bus) =>
        {
            var published = await bus.PublishAsync(new PropagationMessage("http-bus-consumer-rest"), CancellationToken.None);
            return published.IsSuccess ? Results.Ok() : Results.Problem(published.Error.Message);
        });

        await app.StartAsync();
        await app.Services.GetRequiredService<MassTransit.Testing.ITestHarness>().Start();
        return new FrontService(app);
    }

    public static void AddDownstreamRestClient(IServiceCollection services, IConfiguration configuration, DownstreamService downstream) =>
        services.AddSharedKernelCommunication(configuration).AddRestClient<DownstreamRestClient>(
            "downstream",
            client => client
                .Configure(o =>
                {
                    o.BaseAddress = new Uri("http://downstream");
                    o.PropagateIdempotencyKey = true;
                })
                .HttpClientBuilder.ConfigurePrimaryHttpMessageHandler(downstream.CreateHandler));

    public ValueTask DisposeAsync() => _app.DisposeAsync();
}

/// <summary>
/// A worker service whose recurring job calls the downstream service — the job-to-REST hop. The clock is fake, so
/// the job fires when the test advances it.
/// </summary>
internal sealed class JobService : IAsyncDisposable
{
    public const string JobName = "propagation-job";

    private readonly IHost _host;

    private JobService(IHost host, FakeClock clock)
    {
        _host = host;
        Clock = clock;
    }

    public FakeClock Clock { get; }

    public static async Task<JobService> StartAsync(DownstreamService downstream, TenantId tenant, DateTimeOffset start)
    {
        var builder = Host.CreateApplicationBuilder();
        var clock = new FakeClock(start);
        builder.Services.AddSingleton<IClock>(clock);
        builder.Services.AddSharedKernelApplication(typeof(CallDownstreamCommand).Assembly, app => app.UseMediatR());
        FrontService.AddDownstreamRestClient(builder.Services, builder.Configuration, downstream);

        builder.Services
            .AddSharedKernelScheduling(o => o.TickInterval = TimeSpan.FromMilliseconds(150))
            .AddRecurring(JobName, "* * * * * ?", _ => new CallDownstreamCommand(), options =>
            {
                options.MisfirePolicy = MisfirePolicy.Skip;
                options.OverlapPolicy = OverlapPolicy.Skip;
                options.TenantScope = TenantScope.For(tenant);
            });

        var host = builder.Build();
        await host.StartAsync();
        return new JobService(host, clock);
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }
}
