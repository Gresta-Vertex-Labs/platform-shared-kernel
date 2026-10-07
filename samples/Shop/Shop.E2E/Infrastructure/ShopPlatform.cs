using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Serialization;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Grpc.Net.Client;
using Shop.AppHost;
using Xunit;

namespace Shop.E2E.Infrastructure;

/// <summary>
/// The whole platform, started once for every test of the run: the real Shop.AppHost with every container. Starting
/// it takes minutes (images, model downloads, migrations), so the tests share it and isolate themselves by data.
/// </summary>
public sealed class ShopPlatform : IAsyncLifetime
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(15);
    private readonly Dictionary<string, string> _tokens = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private DistributedApplication? _app;

    /// <summary>The running application; <see langword="null"/> when the E2E run is switched off.</summary>
    public DistributedApplication App =>
        _app ?? throw new InvalidOperationException("The platform is not running.");

    public async Task InitializeAsync()
    {
        if (!E2EFactAttribute.Enabled)
        {
            return;
        }

        using var startup = new CancellationTokenSource(StartupTimeout);
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Shop_AppHost>(
            startup.Token
        );
        _app = await builder.BuildAsync(startup.Token);

        // A service or container that dies while starting would leave everything waiting on it until the timeout; stop
        // as soon as one does, and name it.
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(startup.Token);
        string? died = null;
        var watcher = WatchForCrashesAsync(_app, reason => died ??= reason, stop);

        try
        {
            await _app.StartAsync(stop.Token);
            foreach (
                string service in new[]
                {
                    ShopResources.Catalog,
                    ShopResources.Catalog2,
                    ShopResources.Inventory,
                    ShopResources.Inventory2,
                    ShopResources.Ordering,
                    ShopResources.Billing,
                    ShopResources.Merchant,
                }
            )
            {
                await _app.ResourceNotifications.WaitForResourceHealthyAsync(service, stop.Token);
            }
        }
        catch (OperationCanceledException) when (died is not null)
        {
            throw new InvalidOperationException(
                $"The platform did not start: {died}. Run the AppHost (dotnet run --project samples/Shop/Shop.AppHost) "
                    + "and read that resource's logs in the dashboard."
            );
        }
        finally
        {
            await stop.CancelAsync();
            await watcher;
        }
    }

    private static async Task WatchForCrashesAsync(
        DistributedApplication app,
        Action<string> report,
        CancellationTokenSource stop
    )
    {
        try
        {
            await foreach (var update in app.ResourceNotifications.WatchAsync(stop.Token))
            {
                string? state = update.Snapshot.State?.Text;
                if (
                    update.Resource is ProjectResource or ContainerResource
                    && (
                        state == KnownResourceStates.Exited
                        || state == KnownResourceStates.FailedToStart
                    )
                )
                {
                    report(
                        $"{update.Resource.Name} {state.ToLowerInvariant()} (exit code {update.Snapshot.ExitCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"})"
                    );
                    await stop.CancelAsync();
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Startup finished (or timed out); stop watching.
        }
    }

    /// <summary>
    /// A gRPC channel to <paramref name="service"/>'s HTTPS endpoint that trusts only the Shop's development CA and
    /// presents <paramref name="client"/>'s certificate (none when null), as another Shop service would.
    /// </summary>
    public GrpcChannel GrpcChannel(string service, string? client)
    {
        var pki = ShopPki.Ensure(ShopResources.Clients.Ordering, ShopResources.Clients.Rogue);
        var ca = X509CertificateLoader.LoadCertificateFromFile(pki.CaCertificatePath);
        var handler = new SocketsHttpHandler();
        handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, _) =>
        {
            if (certificate is null)
            {
                return false;
            }

            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(ca);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            return chain.Build(new X509Certificate2(certificate));
        };
        if (client is not null)
        {
            handler.SslOptions.ClientCertificates =
            [
                X509CertificateLoader.LoadPkcs12FromFile(
                    pki.ClientCertificatePath(client),
                    ShopPki.Password
                ),
            ];
        }

        return Grpc.Net.Client.GrpcChannel.ForAddress(
            App.GetEndpoint(service, "https"),
            new GrpcChannelOptions { HttpHandler = handler, DisposeHttpClient = true }
        );
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        _tokenLock.Dispose();
    }

    /// <summary>An HTTP client for <paramref name="service"/>, signed in as <paramref name="user"/> (anonymous when null).</summary>
    public async Task<HttpClient> ClientAsync(
        string service,
        string? user = null,
        string? culture = null,
        string? endpoint = null
    )
    {
        var client = endpoint is null
            ? App.CreateHttpClient(service)
            : App.CreateHttpClient(service, endpoint);
        if (user is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                await TokenAsync(user)
            );
        }

        if (culture is not null)
        {
            client.DefaultRequestHeaders.AcceptLanguage.Add(
                new StringWithQualityHeaderValue(culture)
            );
        }

        return client;
    }

    /// <summary>A Keycloak access token for one of the realm's test users (resource-owner password grant).</summary>
    public async Task<string> TokenAsync(string user)
    {
        await _tokenLock.WaitAsync();
        try
        {
            if (_tokens.TryGetValue(user, out string? cached))
            {
                return cached;
            }

            using var keycloak = App.CreateHttpClient(ShopResources.Keycloak, "http");
            using var response = await keycloak.PostAsync(
                $"/realms/{ShopResources.Identity.Realm}/protocol/openid-connect/token",
                new FormUrlEncodedContent(
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["grant_type"] = "password",
                        ["client_id"] = ShopResources.Identity.Client,
                        ["username"] = user,
                        ["password"] = ShopResources.Identity.Password,
                        ["scope"] = "openid",
                    }
                )
            );
            response.EnsureSuccessStatusCode();
            var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
            _tokens[user] = token!.AccessToken;
            return token.AccessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken
    );
}

/// <summary>Every E2E test class shares one running platform.</summary>
[CollectionDefinition(Name)]
public sealed class ShopPlatformCollection : ICollectionFixture<ShopPlatform>
{
    public const string Name = "Shop platform";
}
