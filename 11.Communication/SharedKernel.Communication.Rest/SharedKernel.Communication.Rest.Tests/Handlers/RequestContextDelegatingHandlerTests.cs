using System.Diagnostics;
using System.Net;
using SharedKernel.Communication.Rest.Handlers;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Propagation;

namespace SharedKernel.Communication.Rest.Tests.Handlers;

/// <summary>
/// P-566: <see cref="RequestContextDelegatingHandler"/> writes the ambient caller — correlation id, tenant, actor,
/// client — onto every outbound request, reading <see cref="IRequestContextAccessor"/> rather than an
/// <c>IHttpContextAccessor</c>.
/// </summary>
public sealed class RequestContextDelegatingHandlerTests
{
    private static async Task<HttpRequestMessage> SendAsync(HttpRequestMessage? request = null)
    {
        var capture = new CapturingHandler();
        var client = new HttpClient(new RequestContextDelegatingHandler(new RequestContextAccessor()) { InnerHandler = capture })
        {
            BaseAddress = new Uri("http://localhost"),
        };

        await client.SendAsync(request ?? new HttpRequestMessage(HttpMethod.Get, "/test"));
        return capture.Request!;
    }

    private static string? Header(HttpRequestMessage request, string name)
        => request.Headers.TryGetValues(name, out var values) ? values.Single() : null;

    [Fact]
    public async Task SendAsync_AmbientCaller_WritesEveryPropagationHeader()
    {
        var tenant = new TenantId(Guid.NewGuid());
        using var scope = RequestContextScope.Begin(
            new PropagatedRequestContext(tenant, "user-1", ActorKind.User, "spa", "corr-from-caller"));

        var sent = await SendAsync();

        Header(sent, WellKnownHeaders.CorrelationId).Should().Be("corr-from-caller");
        Header(sent, WellKnownHeaders.TenantId).Should().Be(tenant.ToString());
        Header(sent, WellKnownHeaders.ActorId).Should().Be("user-1");
        Header(sent, WellKnownHeaders.ActorKind).Should().Be("User");
        Header(sent, WellKnownHeaders.ClientId).Should().Be("spa");
    }

    /// <summary>Defect 4: the caller's correlation id is sent, never the Activity's id.</summary>
    [Fact]
    public async Task SendAsync_WithActiveActivity_SendsTheCallersCorrelationIdNotTheActivityId()
    {
        using var activity = new Activity("outbound").Start();
        using var scope = RequestContextScope.Begin(new SystemRequestContext([], correlationId: "caller-id"));

        var sent = await SendAsync();

        Header(sent, WellKnownHeaders.CorrelationId).Should().Be("caller-id").And.NotBe(activity.Id);
    }

    [Fact]
    public async Task SendAsync_NoAmbientCaller_CreatesACorrelationIdAndNoIdentityHeaders()
    {
        var sent = await SendAsync();

        Guid.TryParseExact(Header(sent, WellKnownHeaders.CorrelationId)!, "D", out _).Should().BeTrue();
        Header(sent, WellKnownHeaders.TenantId).Should().BeNull();
        Header(sent, WellKnownHeaders.ActorKind).Should().BeNull();
    }

    [Fact]
    public async Task SendAsync_CallerSetHeaders_AreNeverOverwritten()
    {
        using var scope = RequestContextScope.Begin(
            new PropagatedRequestContext(new TenantId(Guid.NewGuid()), correlationId: "ambient"));
        var request = new HttpRequestMessage(HttpMethod.Get, "/test");
        var explicitTenant = Guid.NewGuid().ToString();
        request.Headers.Add(WellKnownHeaders.CorrelationId, "explicit");
        request.Headers.Add(WellKnownHeaders.TenantId, explicitTenant);

        var sent = await SendAsync(request);

        Header(sent, WellKnownHeaders.CorrelationId).Should().Be("explicit");
        Header(sent, WellKnownHeaders.TenantId).Should().Be(explicitTenant);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
