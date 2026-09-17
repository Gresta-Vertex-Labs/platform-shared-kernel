using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel.Security.Abstractions;
using SharedKernel.Testing.Logging;

namespace SharedKernel.Security.ApiKey.Tests.TestSupport;

internal sealed class ApiKeyTestHost : IAsyncDisposable
{
    private ApiKeyTestHost(IHost host, InMemoryLoggerFactory loggerFactory)
    {
        Host = host;
        LoggerFactory = loggerFactory;
        Client = host.GetTestClient();
    }

    public IHost Host { get; }

    public InMemoryLoggerFactory LoggerFactory { get; }

    public HttpClient Client { get; }

    public TestServer Server => Host.GetTestServer();

    public IReadOnlyList<LogRecord> LogRecords => [.. LoggerFactory.Loggers.Values.SelectMany(logger => logger.Records)];

    public static async Task<ApiKeyTestHost> StartAsync(Action<IServiceCollection> configureServices)
    {
        var loggerFactory = new InMemoryLoggerFactory();
        IHost host = new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddSingleton<ILoggerFactory>(loggerFactory);
                    services.AddRouting();
                    services.AddAuthorization();
                    configureServices(services);
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(MapEndpoints);
                }))
            .Build();

        await host.StartAsync();
        return new ApiKeyTestHost(host, loggerFactory);
    }

    public HttpRequestMessage Get(string path, string? apiKey = null, string headerName = ApiKeyAuthenticationDefaults.HeaderName)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (apiKey is not null)
        {
            request.Headers.TryAddWithoutValidation(headerName, apiKey);
        }

        return request;
    }

    public async Task<CallerSnapshot> GetCallerAsync(HttpRequestMessage request)
    {
        using HttpResponseMessage response = await Client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CallerSnapshot>())!;
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await Host.StopAsync();
        Host.Dispose();
    }

    private static void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/caller", Describe);
        endpoints.MapGet("/protected", Describe).RequireAuthorization();
        endpoints.MapGet("/authenticate", async (HttpContext context) =>
        {
            AuthenticateResult result = await context.AuthenticateAsync();
            return new AuthenticateSnapshot(result.Succeeded, result.None, result.Failure is not null, result.Ticket?.AuthenticationScheme);
        });
    }

    private static CallerSnapshot Describe(HttpContext context)
    {
        IUserContext user = context.RequestServices.GetRequiredService<IUserContext>();
        ITenantProvider tenant = context.RequestServices.GetRequiredService<ITenantProvider>();
        return new CallerSnapshot(
            user.IdentityKind,
            user.IsAuthenticated,
            user.SubjectId,
            user.ClientId,
            user.TenantId,
            [.. user.Roles],
            [.. user.Permissions],
            user.FindClaim(ApiKeyAuthenticationDefaults.KeyIdClaimType),
            tenant.TenantId,
            context.User.Identity?.AuthenticationType,
            context.User.IsInRole("admin"));
    }
}

internal sealed record AuthenticateSnapshot(bool Succeeded, bool None, bool Failed, string? Scheme);

internal sealed record CallerSnapshot(
    [property: JsonConverter(typeof(JsonStringEnumConverter<IdentityKind>))] IdentityKind IdentityKind,
    bool IsAuthenticated,
    string? SubjectId,
    string? ClientId,
    Guid? TenantId,
    string[] Roles,
    string[] Permissions,
    string? KeyId,
    Guid ProviderTenantId,
    string? AuthenticationType,
    bool IsInAdminRole);
