using SharedKernel.Execution.Context;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
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

namespace SharedKernel.Security.Mtls.Tests.TestSupport;

internal sealed class MtlsTestHost : IAsyncDisposable
{
    // TestServer has no TLS handshake; this header stands in for the certificate the connection would carry.
    private const string CertificateHeader = "X-Test-Client-Certificate";

    private MtlsTestHost(IHost host, InMemoryLoggerFactory loggerFactory)
    {
        Host = host;
        LoggerFactory = loggerFactory;
        Client = host.GetTestClient();
        Client.BaseAddress = new Uri("https://localhost/");
    }

    public IHost Host { get; }

    public InMemoryLoggerFactory LoggerFactory { get; }

    public HttpClient Client { get; }

    public IReadOnlyList<LogRecord> LogRecords => [.. LoggerFactory.Loggers.Values.SelectMany(logger => logger.Records)];

    public static IHost Build(Action<IServiceCollection> configureServices, InMemoryLoggerFactory? loggerFactory = null) =>
        new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    if (loggerFactory is not null)
                    {
                        services.AddSingleton<ILoggerFactory>(loggerFactory);
                    }

                    services.AddRouting();
                    services.AddAuthorization();
                    configureServices(services);
                })
                .Configure(app =>
                {
                    app.Use((context, next) =>
                    {
                        if (context.Request.Headers.TryGetValue(CertificateHeader, out var value))
                        {
                            context.Connection.ClientCertificate = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(value!));
                        }

                        return next(context);
                    });
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(MapEndpoints);
                }))
            .Build();

    public static async Task<MtlsTestHost> StartAsync(Action<IServiceCollection> configureServices)
    {
        var loggerFactory = new InMemoryLoggerFactory();
        IHost host = Build(configureServices, loggerFactory);
        await host.StartAsync();
        return new MtlsTestHost(host, loggerFactory);
    }

    public async Task<HttpResponseMessage> SendAsync(string path, X509Certificate2? certificate)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (certificate is not null)
        {
            request.Headers.Add(CertificateHeader, Convert.ToBase64String(certificate.RawData));
        }

        return await Client.SendAsync(request);
    }

    public async Task<CallerSnapshot> GetCallerAsync(string path, X509Certificate2? certificate)
    {
        using HttpResponseMessage response = await SendAsync(path, certificate);
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
        endpoints.MapGet("/certificate", Describe)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = MtlsAuthenticationDefaults.AuthenticationScheme });
        endpoints.MapGet("/default", Describe);
    }

    private static CallerSnapshot Describe(HttpContext context)
    {
        IUserContext user = context.RequestServices.GetRequiredService<IUserContext>();
        return new CallerSnapshot(
            user.ActorKind,
            user.IsAuthenticated,
            user.SubjectId,
            user.ClientId,
            user.TenantId?.Value,
            [.. user.Roles],
            [.. user.Permissions],
            user.FindClaim(MtlsAuthenticationDefaults.CertificateThumbprintClaimType),
            user.FindClaim("extra"),
            context.User.Identity?.AuthenticationType);
    }
}

internal sealed record CallerSnapshot(
    [property: JsonConverter(typeof(JsonStringEnumConverter<ActorKind>))] ActorKind ActorKind,
    bool IsAuthenticated,
    string? SubjectId,
    string? ClientId,
    Guid? TenantId,
    string[] Roles,
    string[] Permissions,
    string? Thumbprint,
    string? Extra,
    string? AuthenticationType);
