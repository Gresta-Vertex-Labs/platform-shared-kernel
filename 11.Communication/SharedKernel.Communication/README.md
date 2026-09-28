# SharedKernel.Communication

The shared base of SharedKernel's outbound clients. You rarely reference it directly: `SharedKernel.Communication.Rest`
and `SharedKernel.Communication.Grpc` bring it, and every client they register gets what is here.

- **One registration, settings in configuration.** `services.AddSharedKernelCommunication(configuration)` and one
  `AddRestClient`/`AddGrpcClient` per service you call. Each client's address, timeouts, retries and credentials are
  read from `SharedKernel:Communication:Clients:{name}` and validated when the host starts.
- **Service discovery** through [`Microsoft.Extensions.ServiceDiscovery`](https://learn.microsoft.com/dotnet/core/extensions/service-discovery)
  (MIT, the library .NET Aspire uses): endpoints from configuration in development, Kubernetes DNS or DNS SRV in the
  cluster, spread round-robin per request.
- **Outbound authentication:** OAuth 2.0 client credentials (cached, refreshed before expiry, one token request at a
  time), an API key, or your own `IAccessTokenProvider`.
- **Mutual TLS:** a client certificate from PEM or PKCS#12 files, and a private certificate authority to trust the server by.
- **The caller travels:** every call carries the ambient caller's correlation id, tenant, actor and client from
  `IRequestContextAccessor`, from an HTTP request, a message consumer, a workflow activity or a job alike.

## Quick start

```csharp
builder.Services.AddSharedKernelCommunication(builder.Configuration)
    .AddRestClient<IInventoryClient, InventoryClient>("inventory")   // SharedKernel.Communication.Rest
    .AddGrpcClient<Pricing.PricingClient>("pricing");                // SharedKernel.Communication.Grpc
```

```json
"SharedKernel": {
  "Communication": {
    "ServiceDiscovery": { "Mode": "Configuration" },
    "Clients": {
      "inventory": { "BaseAddress": "http://inventory" },
      "pricing": { "Address": "http://_grpc.pricing" }
    }
  }
},
"Services": {
  "inventory": { "http": [ "http://localhost:5080" ] },
  "pricing": { "grpc": [ "http://localhost:5081" ] }
}
```

A client name is used once, across REST and gRPC: it is both the `IHttpClientFactory` name and the configuration section.

## Service discovery

A client's address names a service, not a machine: `http://inventory`. How that host becomes endpoints:

| Source | When |
| --- | --- |
| `Services:{service}:{endpoint}` in configuration | Always, first. The shape .NET Aspire and `services__inventory__http__0` environment variables produce. Use it for local development and tests. |
| DNS A/AAAA records | `ServiceDiscovery:Mode = Dns`. Every address of a **headless** Kubernetes service is an endpoint; requests go round-robin, per request. Use it for gRPC, whose long-lived HTTP/2 connections a ClusterIP service would pin to one pod. |
| DNS SRV records | `ServiceDiscovery:Mode = DnsSrv`. The endpoints and ports Kubernetes publishes for a service's named ports (`_grpc._tcp.inventory.shop.svc.cluster.local`). |
| The host as written | Always, last: `http://inventory.shop.svc.cluster.local` is resolved by the operating system; a ClusterIP service balances each connection. |

- `http://_grpc.inventory` picks the service's endpoint named `grpc`; `https+http://inventory` prefers HTTPS when the
  service has an HTTPS endpoint.
- Endpoints are resolved again every `ServiceDiscovery:RefreshPeriod` (60 s), so pods that come and go are followed.
- In the DNS modes the request goes to a pod's address while the `Host` header and the TLS server name stay the
  service's name.

## Authentication

Per client, under `Clients:{name}:Authentication`:

```json
"Authentication": {
  "Mode": "ClientCredentials",
  "ClientCredentials": {
    "TokenEndpoint": "https://login.example.com/oauth2/token",
    "ClientId": "checkout",
    "ClientSecret": "(from a secret store)",
    "Scope": "inventory.read inventory.reserve"
  }
}
```

| Mode | What is sent |
| --- | --- |
| `None` | Nothing (default). A request that sets its own `Authorization` header keeps it. |
| `ClientCredentials` | `Authorization: Bearer {token}` from the RFC 6749 client-credentials grant. The token is cached per credential and fetched again `RefreshBeforeExpiry` (60 s) before it expires — or half-way through a short-lived one; concurrent callers share one token request; a 401 is answered once with a new token. `SecretTransport`: `BasicAuthentication` (default) or `RequestBody`. `Audience`/`Resource` for authorization servers that need them. |
| `ApiKey` | `ApiKey:HeaderName` (default `X-Api-Key`, the header `SharedKernel.Security.ApiKey` reads) with `ApiKey:Value`. |
| `AccessTokenProvider` | A token from your `IAccessTokenProvider`, set with `client.UseAccessTokenProvider<T>()` — for a cloud managed identity, a token exchange, a workload identity. |

When no token can be had the request is not sent: the call fails with `communication.access_token_unavailable`
(`AccessTokenUnavailableException`, an `HttpRequestException`). The credential runs inside the retry loop, so every
attempt carries a valid token. Secrets are read per request, so a rotated key or secret takes effect without a restart.

## Mutual TLS

```json
"Tls": {
  "CertificatePath": "/var/run/secrets/tls/tls.crt",
  "PrivateKeyPath": "/var/run/secrets/tls/tls.key",
  "TrustedCertificateAuthoritiesPath": "/var/run/secrets/tls/ca.crt"
}
```

- `CertificatePath` is a PEM certificate (with `PrivateKeyPath`, or the key in the same file) or a PKCS#12 file;
  `CertificatePassword` for a PKCS#12 file or an encrypted key.
- `TrustedCertificateAuthoritiesPath`: the server's certificate must chain to one of these authorities — only these,
  not the machine's store. A host-name mismatch is never accepted. Revocation is not checked.
- Files are read whenever the connection handler is rebuilt (every two minutes), so a certificate rotated on disk by
  cert-manager is picked up without a restart. Missing files fail startup.

## Error codes

`CommunicationErrorCodes` — a call that failed on the caller's side of the wire:

| Code | Type | Meaning |
| --- | --- | --- |
| `communication.unreachable` | Unavailable | No response: refused, reset, unresolvable, TLS failed |
| `communication.timeout` | Timeout | An attempt or the whole call ran out of time |
| `communication.circuit_open` | Unavailable | Refused without being sent while the circuit breaker is open |
| `communication.access_token_unavailable` | Unavailable | No access token; not sent |
| `communication.empty_body` | Unexpected | A success response with no body where one was expected |
| `communication.invalid_body` | Unexpected | A success body that is not valid JSON for the expected type |

A failure the called service reported keeps the service's own code; one without a code is `http.{status}` or `grpc.{status}`.

## Logging

EventIds 11000–11099: 11000 token acquired (Debug), 11001 token endpoint refused the client (Warning), 11002 token
endpoint unreachable (Warning), 11003 no access token, request not sent (Warning), 11004 401 answered with a new token
(Debug). Tokens and secrets are never logged.

## Testing

`SharedKernel.Communication.Testing`: `StubHttpMessageHandler` + `services.UseStubHttpMessageHandler("inventory", stub)`
runs a client's whole pipeline against canned answers; `GrpcCalls` fakes a generated gRPC client's calls.
