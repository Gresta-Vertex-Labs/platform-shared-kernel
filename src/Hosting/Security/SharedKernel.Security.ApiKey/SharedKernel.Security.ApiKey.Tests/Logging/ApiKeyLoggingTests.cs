using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Security.ApiKey.Extensions;
using SharedKernel.Security.ApiKey.Keys;
using SharedKernel.Security.ApiKey.Tests.TestSupport;
using SharedKernel.Security.ApiKey.Validation;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Logging;
using SharedKernel.Testing.Security;
using Xunit;

namespace SharedKernel.Security.ApiKey.Tests.Logging;

public sealed class ApiKeyLoggingTests
{
    private const string Prefix = "acme_live";
    private const string DistinctiveClient = "client-zq81-distinctive";
    private static readonly Guid DistinctiveTenant = Guid.Parse("7d3c0a54-91be-4f0e-8a3c-5b1e6f2d9a77");
    private static readonly EventId KeyRejected = new(12200);

    [Theory]
    [InlineData("Expired")]
    [InlineData("UnknownKey")]
    [InlineData("WrongPrefix")]
    [InlineData("Malformed")]
    [InlineData("custom.code-1_a")]
    public async Task Rejection_StableReasonCode_IsLoggedUnchanged(string reason)
    {
        await using ApiKeyTestHost host = await StartCustomAsync(reason);

        using HttpResponseMessage response = await host.Client.SendAsync(host.Get("/protected", "any-key-value"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        host.LogRecords.ShouldHaveLoggedWithProperty(KeyRejected, "Reason", reason);
    }

    [Fact]
    public async Task Rejection_ReasonOfExactlyMaxLength_IsLoggedUnchanged()
    {
        string reason = new('a', 64);
        await using ApiKeyTestHost host = await StartCustomAsync(reason);

        using HttpResponseMessage response = await host.Client.SendAsync(host.Get("/protected", "any-key-value"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        host.LogRecords.ShouldHaveLoggedWithProperty(KeyRejected, "Reason", reason);
    }

    [Theory]
    [InlineData("client billing-42 is revoked")]
    [InlineData("Expired\nclient=billing-42")]
    [InlineData("Expired\r\nforged-line")]
    [InlineData("billing-42@example.com")]
    [InlineData("CN=billing-42,O=Acme")]
    [InlineData("a 'quoted' value")]
    [InlineData("ünïcode-reason")]
    public async Task Rejection_ReasonNotIdentifierShaped_IsLoggedAsRejected(string reason)
    {
        await using ApiKeyTestHost host = await StartCustomAsync(reason);

        using HttpResponseMessage response = await host.Client.SendAsync(host.Get("/protected", "any-key-value"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        host.LogRecords.ShouldHaveLoggedWithProperty(KeyRejected, "Reason", "Rejected");
        host.LogRecords.ShouldNotLeak(reason);
    }

    [Fact]
    public async Task Rejection_ReasonLongerThanMaxLength_IsLoggedAsRejected()
    {
        string reason = new('a', 65);
        await using ApiKeyTestHost host = await StartCustomAsync(reason);

        using HttpResponseMessage response = await host.Client.SendAsync(host.Get("/protected", "any-key-value"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        host.LogRecords.ShouldHaveLoggedWithProperty(KeyRejected, "Reason", "Rejected");
    }

    [Fact]
    public async Task Sweep_AllPaths_NeverLogSubjectClientTenantOrKey()
    {
        var store = new InMemoryApiKeyStore();
        var clock = new FakeClock(new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero));
        await using ApiKeyTestHost host = await ApiKeyTestHost.StartAsync(services =>
        {
            services.AddSingleton<IClock>(clock);
            services.AddSingleton<IApiKeyStore>(store);
            services.AddManagedApiKeyAuthentication<InMemoryApiKeyStore>(keys => keys.Prefix = Prefix);
        });
        ApiKeyGenerator generator = host.Host.Services.GetRequiredService<ApiKeyGenerator>();
        GeneratedApiKey valid = generator.Generate();
        GeneratedApiKey revoked = generator.Generate();
        GeneratedApiKey expired = generator.Generate();
        foreach (GeneratedApiKey key in new[] { valid, revoked, expired })
        {
            store.Add(new ApiKeyRecord(key.KeyId, key.KeyHash, DistinctiveClient)
            {
                TenantId = new TenantId(DistinctiveTenant),
                Roles = ["distinctive-role-77"],
                ExpiresAt = ReferenceEquals(key, expired) ? clock.UtcNow.AddSeconds(-1) : null,
            });
        }

        store.Revoke(revoked.KeyId, clock.UtcNow.AddMinutes(-1));
        string forged = ApiKeyFormat.Compose(Prefix, valid.KeyId, new string('x', 32));

        foreach (string presented in new[] { valid.Key, revoked.Key, expired.Key, forged, "sk_live_not-managed" })
        {
            using HttpResponseMessage response = await host.Client.SendAsync(host.Get("/protected", presented));
            Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Unauthorized);
        }

        using HttpResponseMessage twoHeaders = await host.Client.SendAsync(TwoHeaders(valid.Key));

        Assert.NotEmpty(host.LogRecords);
        host.LogRecords.ShouldNotLeak(
            DistinctiveClient,
            DistinctiveTenant.ToString(),
            "distinctive-role-77",
            valid.Key,
            valid.Key[^38..],
            revoked.Key[^38..],
            expired.Key[^38..],
            forged[^38..],
            "sk_live_not-managed");
    }

    private static HttpRequestMessage TwoHeaders(string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/protected");
        request.Headers.TryAddWithoutValidation(ApiKeyAuthenticationDefaults.HeaderName, [key, key]);
        return request;
    }

    private static Task<ApiKeyTestHost> StartCustomAsync(string failureReason) =>
        ApiKeyTestHost.StartAsync(services =>
        {
            services.AddSingleton<IApiKeyValidator>(new FixedFailureValidator(failureReason));
            services.AddApiKeyAuthentication<FixedFailureValidator>();
        });

    private sealed class FixedFailureValidator(string reason) : IApiKeyValidator
    {
        public ValueTask<ApiKeyValidationResult> ValidateAsync(string presentedKey, CancellationToken cancellationToken) =>
            ValueTask.FromResult(ApiKeyValidationResult.Failure(reason, keyId: null));
    }
}
