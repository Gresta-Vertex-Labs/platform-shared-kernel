using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ShippingApi;
using Testcontainers.RabbitMq;

namespace ShippingApi.Tests;

/// <summary>
/// The ShippingApi <c>Program</c> running against a real RabbitMQ broker, plus the polling helpers
/// every scenario needs.
/// </summary>
/// <remarks>
/// <para>
/// One broker and one host for the whole suite (<see cref="SampleHostFixture"/>): starting a broker
/// per test would dominate the runtime and prove nothing extra.
/// </para>
/// <para>
/// <c>masstransit/rabbitmq</c> rather than the official image: the sample wires
/// <c>WithDelayedDelivery()</c>, which needs the delayed-message-exchange plugin the official image
/// does not ship.
/// </para>
/// </remarks>
public sealed class SampleHost : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly RabbitMqContainer _broker = new RabbitMqBuilder("masstransit/rabbitmq:4.3.1")
        .WithUsername("guest")
        .WithPassword("guest")
        .Build();

    /// <summary>Web-defaults JSON, matching what the API writes.</summary>
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web);

    /// <summary>Starts the broker, then the service, and waits until it reports ready.</summary>
    /// <remarks>
    /// <para>
    /// <strong>Waiting for readiness is not politeness — it is the contract.</strong> MassTransit's
    /// hosted service starts the bus in the background and returns, so the host is "started" before
    /// the broker connection exists and before any queue has been declared. A message published in
    /// that window goes to an exchange with nothing bound to it and is dropped by the broker,
    /// silently and successfully.
    /// </para>
    /// <para>
    /// That is exactly what <c>AddMessagingReadinessCheck()</c> is for: in production Kubernetes
    /// holds traffic until <c>/health/ready</c> passes. This test suite does the same thing, rather
    /// than sleeping and hoping — a suite that raced the bus would fail a few runs in ten, and the
    /// failure would look like a messaging defect instead of a missing readiness gate.
    /// </para>
    /// </remarks>
    public async Task InitializeAsync()
    {
        await _broker.StartAsync();

        using HttpClient client = CreateClient();
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromMinutes(1);

        while (DateTime.UtcNow < deadline)
        {
            HttpResponseMessage response = await client.GetAsync("/health/ready");
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new InvalidOperationException(
            "ShippingApi never reported ready. The bus could not connect to the test broker.");
    }

    /// <summary>Stops the host, then the broker.</summary>
    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        await _broker.DisposeAsync();
    }

    /// <summary>
    /// A client acting for a tenant and an actor, which the sample reads from two headers in place
    /// of a real identity provider.
    /// </summary>
    /// <param name="tenantId">The tenant to act for, or <see langword="null"/> to act anonymously.</param>
    /// <param name="actorId">The subject id to act as, or <see langword="null"/>.</param>
    /// <returns>An <see cref="HttpClient"/> carrying those headers on every request.</returns>
    public HttpClient Api(Guid? tenantId = null, string? actorId = null)
    {
        HttpClient client = CreateClient();

        if (tenantId is { } tenant)
        {
            client.DefaultRequestHeaders.Add(HeaderRequestContext.TenantHeader, tenant.ToString("D"));
        }

        if (actorId is not null)
        {
            client.DefaultRequestHeaders.Add(HeaderRequestContext.ActorHeader, actorId);
        }

        return client;
    }

    /// <summary>The projection the consumers in this host write, for assertions a read endpoint cannot make.</summary>
    public ShipmentProjection Projection => Services.GetRequiredService<ShipmentProjection>();

    /// <summary>
    /// Polls <paramref name="read"/> until it returns a non-<see langword="null"/> value or the
    /// timeout elapses.
    /// </summary>
    /// <typeparam name="T">What is being waited for.</typeparam>
    /// <param name="read">The read to retry.</param>
    /// <param name="timeout">How long to wait. Defaults to 30 seconds.</param>
    /// <returns>The value, or <see langword="null"/> if it never appeared.</returns>
    /// <remarks>
    /// Polling rather than a fixed delay: the round trip through a real broker takes tens of
    /// milliseconds on a fast machine and seconds on a loaded CI runner, and a sleep long enough
    /// for the second wastes the first on every run.
    /// </remarks>
    public static async Task<T?> EventuallyAsync<T>(Func<Task<T?>> read, TimeSpan? timeout = null)
        where T : class
    {
        DateTime deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));

        while (DateTime.UtcNow < deadline)
        {
            T? value = await read();
            if (value is not null)
            {
                return value;
            }

            await Task.Delay(100);
        }

        return null;
    }

    /// <summary>Dispatches a shipment and returns its id.</summary>
    /// <param name="client">The client to dispatch with, which carries the acting identity.</param>
    /// <param name="carrier">The carrier.</param>
    /// <param name="trackingNumber">The tracking number.</param>
    /// <returns>The new shipment's id.</returns>
    public static async Task<Guid> DispatchAsync(
        HttpClient client,
        string carrier = "acme-freight",
        string trackingNumber = "TRK-1")
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/shipments",
            new DispatchRequest(carrier, trackingNumber));

        response.EnsureSuccessStatusCode();

        DispatchResponse? body = await response.Content.ReadFromJsonAsync<DispatchResponse>(Json);
        return body!.ShipmentId;
    }

    /// <summary>The shape <c>POST /shipments</c> returns.</summary>
    /// <param name="ShipmentId">The new shipment's id.</param>
    public sealed record DispatchResponse(Guid ShipmentId);

    /// <summary>
    /// Points the sample at the test broker. Everything else — the service name, the retry policy,
    /// the consumers — is the sample's own configuration, unmodified.
    /// </summary>
    /// <param name="builder">The web host builder.</param>
    /// <remarks>
    /// Runs the first time a client is created, which is after <see cref="InitializeAsync"/> has
    /// started the broker, so the connection string is available by then.
    /// </remarks>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:rabbitmq", _broker.GetConnectionString());
    }
}

/// <summary>Shares one broker and one host across every scenario class.</summary>
[CollectionDefinition(Name)]
public sealed class SampleHostFixture : ICollectionFixture<SampleHost>
{
    /// <summary>The xUnit collection name.</summary>
    public const string Name = "ShippingApi";
}
