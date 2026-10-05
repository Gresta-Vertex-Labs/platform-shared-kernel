using SharedKernel.Execution.Context;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Security.Abstractions;
using SharedKernel.Security.ApiKey.Extensions;
using SharedKernel.Security.ApiKey.Keys;
using SharedKernel.Security.ApiKey.Options;
using SharedKernel.Security.ApiKey.Tests.TestSupport;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Logging;
using SharedKernel.Testing.Security;
using Xunit;

namespace SharedKernel.Security.ApiKey.Tests.Authentication;

public sealed class ApiKeyAuthenticationEndToEndTests
{
    private const string Prefix = "acme_live";
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

    private readonly InMemoryApiKeyStore _store = new();
    private readonly FakeClock _clock = new(Now);

    [Fact]
    public async Task Request_ValidKeyHeader_AuthenticatesAsServicePrincipal()
    {
        await using ApiKeyTestHost host = await StartManagedAsync();
        GeneratedApiKey key = IssueKey(host, roles: ["admin", "auditor"], permissions: ["orders:read", "orders:write"]);

        CallerSnapshot caller = await host.GetCallerAsync(host.Get("/caller", key.Key));

        Assert.True(caller.IsAuthenticated);
        Assert.Equal(ActorKind.Service, caller.ActorKind);
        Assert.Equal("billing-service", caller.SubjectId);
        Assert.Equal("billing-service", caller.ClientId);
        Assert.Equal(TenantId, caller.TenantId);
        Assert.Equal(["admin", "auditor"], caller.Roles);
        Assert.Equal(["orders:read", "orders:write"], caller.Permissions);
        Assert.Equal(key.KeyId, caller.KeyId);
        Assert.Equal(ApiKeyAuthenticationDefaults.AuthenticationScheme, caller.AuthenticationType);
        Assert.True(caller.IsInAdminRole);
    }

    [Fact]
    public async Task Request_ValidKeyWithoutAdminRole_IsNotInRole()
    {
        await using ApiKeyTestHost host = await StartManagedAsync();
        GeneratedApiKey key = IssueKey(host, roles: ["auditor"]);

        CallerSnapshot caller = await host.GetCallerAsync(host.Get("/caller", key.Key));

        Assert.True(caller.IsAuthenticated);
        Assert.False(caller.IsInAdminRole);
    }

    [Fact]
    public async Task Request_ValidKeyWithSurroundingWhitespace_Authenticates()
    {
        await using ApiKeyTestHost host = await StartManagedAsync();
        GeneratedApiKey key = IssueKey(host);

        HttpContext context = await host.Server.SendAsync(c =>
        {
            c.Request.Method = HttpMethods.Get;
            c.Request.Path = "/protected";
            c.Request.Headers[ApiKeyAuthenticationDefaults.HeaderName] = $"  {key.Key}\t";
        });

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task Request_ValidKeyOnProtectedEndpoint_Returns200()
    {
        await using ApiKeyTestHost host = await StartManagedAsync();
        GeneratedApiKey key = IssueKey(host);

        using HttpResponseMessage response = await host.Client.SendAsync(host.Get("/protected", key.Key));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Request_NoHeader_IsAnonymous()
    {
        await using ApiKeyTestHost host = await StartManagedAsync();

        CallerSnapshot caller = await host.GetCallerAsync(host.Get("/caller"));

        Assert.False(caller.IsAuthenticated);
        Assert.Equal(ActorKind.Anonymous, caller.ActorKind);
        Assert.Null(caller.SubjectId);
        Assert.DoesNotContain(host.LogRecords, r => r.EventId.Id is 12200 or 12201);
    }

    [Fact]
    public async Task Request_NoHeaderOnProtectedEndpoint_Returns401()
    {
        await using ApiKeyTestHost host = await StartManagedAsync();

        using HttpResponseMessage response = await host.Client.SendAsync(host.Get("/protected"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Request_EmptyHeader_IsAnonymousWithoutRejectionLog()
    {
        await using ApiKeyTestHost host = await StartManagedAsync();

        HttpContext context = await host.Server.SendAsync(c =>
        {
            c.Request.Method = HttpMethods.Get;
            c.Request.Path = "/protected";
            c.Request.Headers[ApiKeyAuthenticationDefaults.HeaderName] = "   ";
        });

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        host.LogRecords.ShouldNotHaveLogged(new EventId(12200));
    }

    [Fact]
    public async Task Request_InvalidKey_Returns401AndLogsReasonAndKeyIdButNotKey()
    {
        await using ApiKeyTestHost host = await StartManagedAsync();
        GeneratedApiKey key = IssueKey(host);
        _store.Revoke(key.KeyId, Now.AddMinutes(-1));

        using HttpResponseMessage response = await host.Client.SendAsync(host.Get("/protected", key.Key));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        LogRecord record = host.LogRecords.ShouldHaveLogged(new EventId(12200), LogLevel.Warning);
        Assert.True(record.TryGetProperty("Reason", out object? reason));
        Assert.Equal("Revoked", reason);
        Assert.True(record.TryGetProperty("KeyId", out object? keyId));
        Assert.Equal(key.KeyId, keyId);
        AssertKeyNeverLogged(host, key.Key);
    }

    [Fact]
    public async Task Request_InvalidKeyOnAnonymousEndpoint_IsNotAuthenticated()
    {
        await using ApiKeyTestHost host = await StartManagedAsync();
        GeneratedApiKey key = IssueKey(host, expiresAt: Now.AddSeconds(-1));

        CallerSnapshot caller = await host.GetCallerAsync(host.Get("/caller", key.Key));

        Assert.False(caller.IsAuthenticated);
        Assert.Equal(ActorKind.Anonymous, caller.ActorKind);
        host.LogRecords.ShouldHaveLoggedWithProperty(new EventId(12200), "Reason", "Expired");
    }

    [Fact]
    public async Task Request_MalformedKey_Returns401AndLogsMalformedWithoutKeyId()
    {
        await using ApiKeyTestHost host = await StartManagedAsync();
        const string presented = "sk_live_this-is-not-a-managed-key";

        using HttpResponseMessage response = await host.Client.SendAsync(host.Get("/protected", presented));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        LogRecord record = host.LogRecords.ShouldHaveLoggedWithProperty(new EventId(12200), "Reason", "Malformed");
        Assert.True(record.TryGetProperty("KeyId", out object? keyId));
        Assert.Null(keyId);
        AssertKeyNeverLogged(host, presented);
    }

    [Fact]
    public async Task Request_WrongSecretForKnownKeyId_Returns401()
    {
        await using ApiKeyTestHost host = await StartManagedAsync();
        GeneratedApiKey key = IssueKey(host);
        string forged = ApiKeyFormat.Compose(Prefix, key.KeyId, new string('x', 32));

        using HttpResponseMessage response = await host.Client.SendAsync(host.Get("/protected", forged));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        host.LogRecords.ShouldHaveLoggedWithProperty(new EventId(12200), "Reason", "UnknownKey");
        AssertKeyNeverLogged(host, forged);
    }

    [Fact]
    public async Task Request_TwoHeaderValues_Returns401AndLogsAmbiguousKey()
    {
        await using ApiKeyTestHost host = await StartManagedAsync();
        GeneratedApiKey first = IssueKey(host);
        GeneratedApiKey second = IssueKey(host);

        HttpContext context = await host.Server.SendAsync(c =>
        {
            c.Request.Method = HttpMethods.Get;
            c.Request.Path = "/protected";
            c.Request.Headers[ApiKeyAuthenticationDefaults.HeaderName] = new StringValues([first.Key, second.Key]);
        });

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        host.LogRecords.ShouldHaveLoggedWithProperty(new EventId(12201), "HeaderName", ApiKeyAuthenticationDefaults.HeaderName);
        host.LogRecords.ShouldNotHaveLogged(new EventId(12200));
        AssertKeyNeverLogged(host, first.Key);
        AssertKeyNeverLogged(host, second.Key);
    }

    [Fact]
    public async Task Request_TwoHeaderValuesViaHttpClient_Returns401()
    {
        await using ApiKeyTestHost host = await StartManagedAsync();
        GeneratedApiKey first = IssueKey(host);
        var request = new HttpRequestMessage(HttpMethod.Get, "/protected");
        request.Headers.TryAddWithoutValidation(ApiKeyAuthenticationDefaults.HeaderName, [first.Key, first.Key]);

        using HttpResponseMessage response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Request_CustomHeaderName_AuthenticatesOnlyFromThatHeader()
    {
        await using ApiKeyTestHost host = await StartManagedAsync(scheme => scheme.HeaderName = "X-Partner-Key");
        GeneratedApiKey key = IssueKey(host);

        CallerSnapshot viaCustom = await host.GetCallerAsync(host.Get("/caller", key.Key, "X-Partner-Key"));
        CallerSnapshot viaDefault = await host.GetCallerAsync(host.Get("/caller", key.Key));

        Assert.True(viaCustom.IsAuthenticated);
        Assert.Equal("billing-service", viaCustom.SubjectId);
        Assert.False(viaDefault.IsAuthenticated);
    }

    [Theory]
    [InlineData("api_key")]
    [InlineData("apiKey")]
    [InlineData("X-Api-Key")]
    public async Task Request_KeyInQueryString_IsIgnored(string parameter)
    {
        await using ApiKeyTestHost host = await StartManagedAsync();
        GeneratedApiKey key = IssueKey(host);

        using HttpResponseMessage response = await host.Client.GetAsync($"/protected?{parameter}={Uri.EscapeDataString(key.Key)}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Request_KeyInAuthorizationHeader_IsIgnored()
    {
        await using ApiKeyTestHost host = await StartManagedAsync();
        GeneratedApiKey key = IssueKey(host);
        var request = new HttpRequestMessage(HttpMethod.Get, "/protected");
        request.Headers.TryAddWithoutValidation("Authorization", $"ApiKey {key.Key}");

        using HttpResponseMessage response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Request_KeyExpiresBetweenRequests_SecondRequestIsRejected()
    {
        await using ApiKeyTestHost host = await StartManagedAsync();
        GeneratedApiKey key = IssueKey(host, expiresAt: Now.AddMinutes(1));

        using HttpResponseMessage before = await host.Client.SendAsync(host.Get("/protected", key.Key));
        _clock.Advance(TimeSpan.FromMinutes(1));
        using HttpResponseMessage after = await host.Client.SendAsync(host.Get("/protected", key.Key));

        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    public async Task Request_KeyWithOtherEnvironmentPrefix_Returns401()
    {
        await using ApiKeyTestHost host = await StartManagedAsync();
        GeneratedApiKey liveKey = IssueKey(host);
        string testKey = ApiKeyFormat.Compose("acme_test", liveKey.KeyId, liveKey.Key[^38..^6]);

        using HttpResponseMessage response = await host.Client.SendAsync(host.Get("/protected", testKey));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        host.LogRecords.ShouldHaveLoggedWithProperty(new EventId(12200), "Reason", "WrongPrefix");
    }

    private Task<ApiKeyTestHost> StartManagedAsync(Action<ApiKeyAuthenticationOptions>? configureScheme = null) =>
        ApiKeyTestHost.StartAsync(services =>
        {
            services.AddSingleton<IClock>(_clock);
            services.AddSingleton<IApiKeyStore>(_store);
            services.AddManagedApiKeyAuthentication<InMemoryApiKeyStore>(keys => keys.Prefix = Prefix, configureScheme);
        });

    private GeneratedApiKey IssueKey(
        ApiKeyTestHost host,
        IReadOnlyCollection<string>? roles = null,
        IReadOnlyCollection<string>? permissions = null,
        DateTimeOffset? expiresAt = null)
    {
        GeneratedApiKey key = host.Host.Services.GetRequiredService<ApiKeyGenerator>().Generate();
        _store.Add(new ApiKeyRecord(key.KeyId, key.KeyHash, "billing-service")
        {
            TenantId = new(TenantId),
            Roles = roles ?? [],
            Permissions = permissions ?? [],
            ExpiresAt = expiresAt,
        });
        return key;
    }

    private static void AssertKeyNeverLogged(ApiKeyTestHost host, string key)
    {
        string secretPart = key.Length > 38 ? key[^38..] : key;
        foreach (LogRecord record in host.LogRecords)
        {
            Assert.DoesNotContain(secretPart, record.Message, StringComparison.Ordinal);
            foreach (KeyValuePair<string, object?> property in record.State ?? [])
            {
                Assert.DoesNotContain(secretPart, property.Value?.ToString() ?? string.Empty, StringComparison.Ordinal);
            }
        }
    }
}
