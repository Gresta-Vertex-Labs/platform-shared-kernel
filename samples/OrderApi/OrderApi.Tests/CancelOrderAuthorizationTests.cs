using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderApi.Application.Features.Orders;
using SharedKernel.Execution.Context;
using SharedKernel.Security.Abstractions;
using Xunit;

namespace OrderApi.Tests;

/// <summary>
/// <c>POST /orders/{id}/cancel</c> sends <see cref="CancelOrderCommand"/>, which declares
/// <c>[RequirePermission("orders.cancel")]</c>. The endpoint declares nothing: these tests prove, over HTTP, that the
/// application pipeline enforces the use case's permission against the <see cref="IRequestContext"/> that
/// <c>AddSharedKernelRequestContext()</c> builds from the authenticated caller.
/// </summary>
/// <remarks>
/// The Api itself has no identity provider (every caller is anonymous). The test host adds a header-driven test
/// authentication scheme and maps its principal to the <see cref="IUserContext"/> the request context reads, exactly
/// what an authentication package of <c>12.Security</c> (<c>AddOidcAuthentication</c>) registers in a real service.
/// </remarks>
public sealed class CancelOrderAuthorizationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public CancelOrderAuthorizationTests(WebApplicationFactory<Program> factory) =>
        _client = factory.WithWebHostBuilder(host => host.ConfigureTestServices(services =>
        {
            services.AddAuthentication(TestAuthentication.Scheme)
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthentication.Scheme, _ => { });
            services.AddHttpContextAccessor();
            services.RemoveAll<IUserContext>();
            services.AddScoped(sp => TestAuthentication.ToUserContext(
                sp.GetRequiredService<IHttpContextAccessor>().HttpContext?.User));
        })).CreateClient();

    [Fact]
    public async Task Anonymous_IsUnauthorized()
    {
        var id = await PlaceOrderAsync();

        var response = await CancelAsync(id, user: null);

        await ShouldBeProblemAsync(response, HttpStatusCode.Unauthorized, "unauthorized.default");
        (await GetCancelledAsync(id)).Should().BeFalse();
    }

    [Fact]
    public async Task AuthenticatedWithoutThePermission_IsForbidden()
    {
        var id = await PlaceOrderAsync();

        var response = await CancelAsync(id, user: "clerk-1", "orders.read");

        await ShouldBeProblemAsync(response, HttpStatusCode.Forbidden, "forbidden.insufficient_permission");
        (await GetCancelledAsync(id)).Should().BeFalse();
    }

    [Fact]
    public async Task WithThePermission_CancelsTheOrder()
    {
        var id = await PlaceOrderAsync();

        var response = await CancelAsync(id, user: "supervisor-1", OrderPermissions.Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetCancelledAsync(id)).Should().BeTrue();

        var again = await CancelAsync(id, user: "supervisor-1", OrderPermissions.Cancel);
        await ShouldBeProblemAsync(again, HttpStatusCode.Conflict, "order.already_cancelled");
    }

    [Fact]
    public async Task WithThePermission_UnknownOrder_IsNotFound()
    {
        var response = await CancelAsync(Guid.CreateVersion7(), user: "supervisor-1", OrderPermissions.Cancel);

        await ShouldBeProblemAsync(response, HttpStatusCode.NotFound, "order.notFound");
    }

    private async Task<Guid> PlaceOrderAsync()
    {
        var created = await _client.PostAsJsonAsync("/orders",
            new { customer = "Acme Ltd", amount = 20m, currency = "EUR", lines = new[] { "Widget" } });
        created.StatusCode.Should().Be(HttpStatusCode.Created, "placing an order needs no permission");
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<bool> GetCancelledAsync(Guid id) =>
        (await _client.GetFromJsonAsync<JsonElement>($"/orders/{id}")).GetProperty("cancelled").GetBoolean();

    private Task<HttpResponseMessage> CancelAsync(Guid id, string? user, params string[] permissions)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/orders/{id}/cancel");
        if (user is not null)
        {
            request.Headers.Add(TestAuthentication.UserHeader, user);
            request.Headers.Add(TestAuthentication.PermissionsHeader, string.Join(',', permissions));
        }

        return _client.SendAsync(request);
    }

    private static async Task ShouldBeProblemAsync(HttpResponseMessage response, HttpStatusCode status, string errorCode)
    {
        response.StatusCode.Should().Be(status);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString()
            .Should().Be(errorCode);
    }
}

/// <summary>A test-only authentication scheme: the caller states its subject and permissions in headers.</summary>
internal static class TestAuthentication
{
    public const string Scheme = "Test";
    public const string UserHeader = "X-Test-User";
    public const string PermissionsHeader = "X-Test-Permissions";
    private const string PermissionClaim = "permission";

    public static ClaimsPrincipal? Authenticate(HttpRequest request)
    {
        var user = request.Headers[UserHeader].ToString();
        if (string.IsNullOrWhiteSpace(user))
            return null;

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, user) };
        claims.AddRange(request.Headers[PermissionsHeader].ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(permission => new Claim(PermissionClaim, permission)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme));
    }

    /// <summary>What an authentication package's <c>IUserContext</c> registration does: the principal, or anonymous.</summary>
    public static IUserContext ToUserContext(ClaimsPrincipal? principal)
    {
        var subject = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (principal?.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(subject))
            return AnonymousUserContext.Instance;

        return new UserContext(ActorKind.User, subject, principal.Claims)
        {
            Permissions = [.. principal.FindAll(PermissionClaim).Select(c => c.Value)],
        };
    }
}

internal sealed class TestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
        Task.FromResult(TestAuthentication.Authenticate(Request) is { } principal
            ? AuthenticateResult.Success(new AuthenticationTicket(principal, TestAuthentication.Scheme))
            : AuthenticateResult.NoResult());
}
