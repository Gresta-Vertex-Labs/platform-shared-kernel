using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Security.Abstractions;
using SharedKernel.Security.ApiKey.Authentication;
using SharedKernel.Security.ApiKey.Extensions;
using SharedKernel.Security.ApiKey.Keys;
using SharedKernel.Security.ApiKey.Tests.TestSupport;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Security;
using Xunit;

namespace SharedKernel.Security.ApiKey.Tests.Authentication;

public sealed class ApiKeyForwardingSchemeTests
{
    private readonly InMemoryApiKeyStore _store = new();

    public static TheoryData<bool> BearerRegisteredFirst => new() { true, false };

    [Theory]
    [MemberData(nameof(BearerRegisteredFirst))]
    public async Task Request_WithApiKeyHeader_IsAuthenticatedByApiKey(bool bearerFirst)
    {
        await using ApiKeyTestHost host = await StartWithBearerAsync(bearerFirst);
        GeneratedApiKey key = IssueKey(host);

        CallerSnapshot caller = await host.GetCallerAsync(host.Get("/caller", key.Key));

        Assert.Equal(ApiKeyAuthenticationDefaults.AuthenticationScheme, caller.AuthenticationType);
        Assert.Equal(IdentityKind.ServicePrincipal, caller.IdentityKind);
        Assert.Equal("billing-service", caller.SubjectId);
    }

    [Theory]
    [MemberData(nameof(BearerRegisteredFirst))]
    public async Task Request_WithoutApiKeyHeader_IsAuthenticatedByBearer(bool bearerFirst)
    {
        await using ApiKeyTestHost host = await StartWithBearerAsync(bearerFirst);
        HttpRequestMessage request = host.Get("/caller");
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {FakeBearerHandler.ValidToken}");

        CallerSnapshot caller = await host.GetCallerAsync(request);

        Assert.Equal(FakeBearerHandler.SchemeName, caller.AuthenticationType);
        // No mapper handles the fake Bearer scheme, so the resolver treats it as anonymous.
        Assert.Equal(IdentityKind.Anonymous, caller.IdentityKind);
    }

    [Theory]
    [MemberData(nameof(BearerRegisteredFirst))]
    public async Task Request_WhitespaceApiKeyHeader_IsAuthenticatedByBearer(bool bearerFirst)
    {
        await using ApiKeyTestHost host = await StartWithBearerAsync(bearerFirst);
        HttpRequestMessage request = host.Get("/caller");
        request.Headers.TryAddWithoutValidation(ApiKeyAuthenticationDefaults.HeaderName, " ");
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {FakeBearerHandler.ValidToken}");

        CallerSnapshot caller = await host.GetCallerAsync(request);

        Assert.Equal(FakeBearerHandler.SchemeName, caller.AuthenticationType);
    }

    [Theory]
    [MemberData(nameof(BearerRegisteredFirst))]
    public async Task Request_WithBothCredentials_UsesApiKeyOnly(bool bearerFirst)
    {
        await using ApiKeyTestHost host = await StartWithBearerAsync(bearerFirst);
        GeneratedApiKey key = IssueKey(host);
        HttpRequestMessage request = host.Get("/caller", key.Key);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {FakeBearerHandler.ValidToken}");

        CallerSnapshot caller = await host.GetCallerAsync(request);

        Assert.Equal(ApiKeyAuthenticationDefaults.AuthenticationScheme, caller.AuthenticationType);
    }

    [Theory]
    [MemberData(nameof(BearerRegisteredFirst))]
    public async Task Request_InvalidApiKeyWithValidBearer_IsRejected(bool bearerFirst)
    {
        await using ApiKeyTestHost host = await StartWithBearerAsync(bearerFirst);
        HttpRequestMessage request = host.Get("/protected", "acme_live_not-a-key");
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {FakeBearerHandler.ValidToken}");

        using HttpResponseMessage response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(BearerRegisteredFirst))]
    public async Task Request_NoCredentialsOnProtectedEndpoint_IsChallengedByBearer(bool bearerFirst)
    {
        await using ApiKeyTestHost host = await StartWithBearerAsync(bearerFirst);

        using HttpResponseMessage response = await host.Client.SendAsync(host.Get("/protected"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.ToString());
    }

    [Theory]
    [MemberData(nameof(BearerRegisteredFirst))]
    public async Task Options_BearerDefault_BecomesFallbackOfForwardingScheme(bool bearerFirst)
    {
        await using ApiKeyTestHost host = await StartWithBearerAsync(bearerFirst);

        AuthenticationOptions options = host.Host.Services.GetRequiredService<IOptions<AuthenticationOptions>>().Value;
        ApiKeyForwarding forwarding = host.Host.Services
            .GetServices<IPostConfigureOptions<AuthenticationOptions>>()
            .OfType<ApiKeyForwarding>()
            .Single();

        Assert.Equal(ApiKeyAuthenticationDefaults.ForwardingScheme, options.DefaultScheme);
        Assert.Equal(FakeBearerHandler.SchemeName, forwarding.FallbackScheme);
    }

    [Fact]
    public async Task Request_WithoutHeaderAndNoOtherScheme_GetsNoResult()
    {
        await using ApiKeyTestHost host = await ApiKeyTestHost.StartAsync(services =>
        {
            services.AddSingleton<IClock>(new FakeClock());
            services.AddSingleton<IApiKeyStore>(_store);
            services.AddManagedApiKeyAuthentication<InMemoryApiKeyStore>(keys => keys.Prefix = "acme_live");
        });

        AuthenticateSnapshot result = (await host.Client.GetFromJsonAsync<AuthenticateSnapshot>("/authenticate"))!;
        CallerSnapshot caller = await host.GetCallerAsync(host.Get("/caller"));
        using HttpResponseMessage response = await host.Client.SendAsync(host.Get("/protected"));

        Assert.True(result.None);
        Assert.False(result.Failed);
        Assert.False(caller.IsAuthenticated);
        Assert.Null(caller.AuthenticationType);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(host.Host.Services.GetServices<IPostConfigureOptions<AuthenticationOptions>>().OfType<ApiKeyForwarding>().Single().FallbackScheme);
    }

    [Fact]
    public void PostConfigure_RunTwice_KeepsOriginalFallback()
    {
        var forwarding = new ApiKeyForwarding();
        var options = new AuthenticationOptions { DefaultScheme = "Bearer" };

        forwarding.PostConfigure(Microsoft.Extensions.Options.Options.DefaultName, options);
        forwarding.PostConfigure(Microsoft.Extensions.Options.Options.DefaultName, options);

        Assert.Equal("Bearer", forwarding.FallbackScheme);
        Assert.Equal(ApiKeyAuthenticationDefaults.ForwardingScheme, options.DefaultScheme);
    }

    [Fact]
    public void PostConfigure_NoDefaultScheme_LeavesFallbackNull()
    {
        var forwarding = new ApiKeyForwarding();
        var options = new AuthenticationOptions();

        forwarding.PostConfigure(Microsoft.Extensions.Options.Options.DefaultName, options);

        Assert.Null(forwarding.FallbackScheme);
        Assert.Equal(ApiKeyAuthenticationDefaults.ForwardingScheme, options.DefaultScheme);
    }

    private Task<ApiKeyTestHost> StartWithBearerAsync(bool bearerFirst) =>
        ApiKeyTestHost.StartAsync(services =>
        {
            services.AddSingleton<IClock>(new FakeClock());
            services.AddSingleton<IApiKeyStore>(_store);

            if (bearerFirst)
            {
                AddBearer(services);
            }

            services.AddManagedApiKeyAuthentication<InMemoryApiKeyStore>(keys => keys.Prefix = "acme_live");

            if (!bearerFirst)
            {
                AddBearer(services);
            }
        });

    private static void AddBearer(IServiceCollection services) =>
        services.AddAuthentication(FakeBearerHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, FakeBearerHandler>(FakeBearerHandler.SchemeName, _ => { });

    private GeneratedApiKey IssueKey(ApiKeyTestHost host)
    {
        GeneratedApiKey key = host.Host.Services.GetRequiredService<ApiKeyGenerator>().Generate();
        _store.Add(key, "billing-service");
        return key;
    }
}
