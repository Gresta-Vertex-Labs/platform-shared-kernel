using SharedKernel.Execution.Context;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Security.Abstractions;
using SharedKernel.Security.Oidc.Extensions;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Logging;

namespace SharedKernel.Security.Oidc.Tests.Infrastructure;

internal sealed class OidcTestHostOptions
{
    public Dictionary<string, string?> Settings { get; } = new(StringComparer.Ordinal)
    {
        ["SharedKernel:Security:Oidc:Authority"] = TestTokens.Issuer,
        ["SharedKernel:Security:Oidc:Audiences:0"] = TestTokens.Audience,
    };

    public List<SecurityKey> SigningKeys { get; } = [TestTokens.RsaKey, TestTokens.EcKey, TestTokens.SymmetricKey];

    public Action<IServiceCollection>? BeforeOidc { get; set; }

    public Action<OidcAuthenticationBuilder>? Oidc { get; set; }

    public Action<IServiceCollection>? AfterOidc { get; set; }

    public Action<HttpContext>? BeforeAuthentication { get; set; }
}

internal sealed class OidcTestHost : IAsyncDisposable
{
    public const string ResourcePath = "/resource";
    public static readonly Uri BaseAddress = new("https://api.example.test/");

    private readonly IHost _host;

    private OidcTestHost(IHost host, FakeClock clock, InMemoryLoggerFactory logs)
    {
        _host = host;
        Clock = clock;
        Logs = logs;
        TestServer server = host.GetTestServer();
        server.BaseAddress = BaseAddress;
        Client = server.CreateClient();
    }

    public HttpClient Client { get; }

    public FakeClock Clock { get; }

    public InMemoryLoggerFactory Logs { get; }

    public IServiceProvider Services => _host.Services;

    public IReadOnlyList<LogRecord> LogRecords => [.. Logs.Loggers.Values.SelectMany(logger => logger.Records)];

    public static IHost Build(OidcTestHostOptions options, FakeClock clock, InMemoryLoggerFactory logs)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(options.Settings).Build();

        return new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.Replace(ServiceDescriptor.Singleton<ILoggerFactory>(logs));
                    services.AddSingleton<IClock>(clock);
                    services.AddDataProtection();
                    services.Configure<KeyManagementOptions>(keys =>
                    {
                        keys.XmlRepository = new InMemoryXmlRepository();
                        keys.XmlEncryptor = null;
                    });
                    services.AddRouting();
                    services.AddAuthorization();

                    options.BeforeOidc?.Invoke(services);
                    OidcAuthenticationBuilder builder = services.AddOidcAuthentication(configuration);
                    options.Oidc?.Invoke(builder);
                    options.AfterOidc?.Invoke(services);

                    // Signing keys come from memory instead of the provider's discovery document.
                    var metadata = new OpenIdConnectConfiguration { Issuer = TestTokens.Issuer };
                    foreach (SecurityKey key in options.SigningKeys)
                    {
                        metadata.SigningKeys.Add(key);
                    }

                    services.PostConfigure<JwtBearerOptions>(OidcAuthenticationDefaults.AuthenticationScheme, jwt =>
                    {
                        jwt.Configuration = metadata;
                        jwt.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata);
                    });
                })
                .Configure(app =>
                {
                    if (options.BeforeAuthentication is { } before)
                    {
                        app.Use((context, next) =>
                        {
                            before(context);
                            return next(context);
                        });
                    }

                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.Map(ResourcePath, Describe).RequireAuthorization();
                        endpoints.Map("/roles/{role}", (HttpContext context, string role) => Results.Json(context.User.IsInRole(role)))
                            .RequireAuthorization();
                    });
                }))
            .Build();
    }

    public static async Task<OidcTestHost> StartAsync(Action<OidcTestHostOptions>? configure = null)
    {
        var options = new OidcTestHostOptions();
        configure?.Invoke(options);

        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var logs = new InMemoryLoggerFactory();
        IHost host = Build(options, clock, logs);
        await host.StartAsync();
        return new OidcTestHost(host, clock, logs);
    }

    public Task<HttpResponseMessage> SendAsync(
        string? token,
        string scheme = "Bearer",
        HttpMethod? method = null,
        string path = ResourcePath,
        params string[] dpopProofs)
    {
        var request = new HttpRequestMessage(method ?? HttpMethod.Get, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(scheme, token);
        }

        if (dpopProofs.Length > 0)
        {
            request.Headers.TryAddWithoutValidation(OidcAuthenticationDefaults.DpopScheme, dpopProofs);
        }

        return Client.SendAsync(request);
    }

    public async Task<UserResponse> GetUserAsync(string token, string scheme = "Bearer", HttpMethod? method = null, params string[] dpopProofs)
    {
        using HttpResponseMessage response = await SendAsync(token, scheme, method, ResourcePath, dpopProofs);
        string body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Expected success but got {(int)response.StatusCode}: {body}");
        }

        return JsonSerializer.Deserialize<UserResponse>(body, JsonSerializerOptions.Web)!;
    }

    public ValueTask DisposeAsync()
    {
        Client.Dispose();
        _host.Dispose();
        return ValueTask.CompletedTask;
    }

    private static IResult Describe(HttpContext context, IUserContext user) =>
        Results.Json(new UserResponse(
            user.ActorKind.ToString(),
            user.IsAuthenticated,
            user.SubjectId,
            user.ClientId,
            user.TenantId?.Value,
            user.SessionId,
            user.Name,
            user.Email,
            [.. user.Roles],
            [.. user.Permissions],
            [.. user.AuthenticationMethods],
            user.AuthContextClassReference,
            user.AuthTime,
            user.IsSenderConstrained,
            context.User.Identity?.Name,
            [.. context.User.Claims.Select(claim => claim.Type).Distinct()],
            context.User.FindFirst(ClaimTypes.NameIdentifier) is not null));
}

internal sealed record UserResponse(
    string Kind,
    bool IsAuthenticated,
    string? SubjectId,
    string? ClientId,
    Guid? TenantId,
    string? SessionId,
    string? Name,
    string? Email,
    string[] Roles,
    string[] Permissions,
    string[] AuthenticationMethods,
    string? AuthContextClassReference,
    DateTimeOffset? AuthTime,
    bool IsSenderConstrained,
    string? PrincipalName,
    string[] ClaimTypes,
    bool HasMappedNameIdentifier);
