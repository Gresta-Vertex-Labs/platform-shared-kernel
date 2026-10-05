# SharedKernel.ServiceDefaults.Security.Mtls

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Host](https://img.shields.io/badge/tier-Host-d73a49)

> **Mutual-TLS host wiring: accept client certificates negotiated by Kestrel, or forwarded as a header by a
> TLS-terminating ingress — and only from the proxies you trust. The accept-or-reject decision stays with
> `SharedKernel.Security.Mtls`'s `IMtlsCertificateValidator`.**

| You get | So that |
| --- | --- |
| `builder.AddMtlsClientCertificate(mode)` | Kestrel asks for (or requires) a client certificate and validates it with your validator |
| `builder.AddMtlsForwardedHeaderCertificate(o => …)` + `MtlsForwardedHeaderMiddleware` | A certificate an ingress forwards becomes `HttpContext.Connection.ClientCertificate` |
| A `TrustedNetworks` allow-list | Only the ingress can set the certificate header; anyone else's header is ignored, never decoded |
| One validator for both paths | The mTLS authentication handler and the transport layer agree on who is trusted |
| Accept/reject events with thumbprint and subject | An audit trail of certificate decisions, never raw certificate bytes |

## Install

```xml
<PackageReference Include="SharedKernel.ServiceDefaults.Security.Mtls" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Host — reference it from your **Api** project |
| Depends on | `SharedKernel.ServiceDefaults`, [`SharedKernel.Security.Mtls`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Hosting/Security/SharedKernel.Security.Mtls/README.md) |
| Namespaces | `SharedKernel.ServiceDefaults.Security` |

## Quick start

Register a validator first — typically through `AddMtlsAuthentication<TValidator>()` from `SharedKernel.Security.Mtls`.
Without one, the Kestrel path throws at the first TLS handshake and the forwarded-header path at the first request.

**TLS terminates at Kestrel:**

```csharp
using Microsoft.AspNetCore.Server.Kestrel.Https;
using SharedKernel.Security.Mtls.Extensions;
using SharedKernel.ServiceDefaults.Security;

builder.Services.AddMtlsAuthentication<PartnerCertificateValidator>();
builder.AddMtlsClientCertificate(ClientCertificateMode.RequireCertificate);   // default: AllowCertificate
```

No middleware is needed; this configures `KestrelServerOptions.ConfigureHttpsDefaults`.

**TLS terminates at an ingress that forwards the certificate as a header:**

```csharp
using System.Net;
using SharedKernel.ServiceDefaults.Security;

builder.AddMtlsForwardedHeaderCertificate(o =>
{
    o.HeaderName = "ssl-client-cert";                         // required — there is no default
    o.AddTrustedNetwork(IPNetwork.Parse("10.0.0.0/16"));       // the ingress-controller subnet
    o.AddTrustedProxy(IPAddress.Parse("10.0.5.7"));            // a single known proxy hop
});

var app = builder.Build();
app.UseSharedKernelRequestContext();
app.UseSharedKernelWebApi(p => p.AtStart(a => a.UseMiddleware<MtlsForwardedHeaderMiddleware>()));
```

Without `SharedKernel.Presentation.WebApi`, add `app.UseMiddleware<MtlsForwardedHeaderMiddleware>()` before
`UseAuthentication()` and before any `UseForwardedHeaders()`.

## How it works

- **Kestrel path.** `ClientCertificateValidation` resolves `IMtlsCertificateValidator` in a new scope and calls it. The
  callback is synchronous while the validator is async, so the call blocks inside the TLS handshake.
- **Forwarded-header path**, per request:
  1. With `TrustedNetworks` configured, a request whose remote IP is outside every network is passed on untouched: the
     header is never decoded and no certificate is set (a rejection is logged, EventId 13001).
  2. The header value is decoded as Base64 DER, or else as URL-encoded PEM (the formats nginx-ingress, Envoy and
     HAProxy use); an undecodable value is ignored.
  3. The validator decides. Accepted → `HttpContext.Connection.ClientCertificate` is set (disposed when the response
     completes) and 13000 is logged; rejected → 13001 and no certificate.
  The middleware never ends the request itself — authentication and authorization decide what a missing certificate means.
- With `TrustedNetworks` empty, every remote address is trusted and a one-time startup warning (EventId 13003) fires.
- `HeaderName` is validated at host start; an empty value stops the host.

## Configuration

Set in code on `AddMtlsForwardedHeaderCertificate(o => …)` (`MtlsForwardedHeaderOptions`).

| Option | Type | Default | Meaning |
| --- | --- | --- | --- |
| `HeaderName` | `string` | — (required) | The header the ingress forwards the certificate in (vendor-specific, so no default) |
| `AddTrustedNetwork(IPNetwork)` | method | none | A subnet whose requests may carry the header |
| `AddTrustedProxy(IPAddress)` | method | none | One proxy address (a /32 or /128 network) |
| `TrustedNetworks` | `IReadOnlyCollection<IPNetwork>` | empty | The allow-list built by the two methods above |

`AddMtlsClientCertificate(ClientCertificateMode mode = AllowCertificate)` takes the Kestrel mode as its only option.

## Reference

| Member | Purpose |
| --- | --- |
| `IHostApplicationBuilder.AddMtlsClientCertificate(ClientCertificateMode)` | Kestrel client-certificate negotiation and validation |
| `IHostApplicationBuilder.AddMtlsForwardedHeaderCertificate(Action<MtlsForwardedHeaderOptions>)` | Options for the forwarded-header path, validated on start |
| `MtlsForwardedHeaderMiddleware` | Decodes, validates and sets the forwarded certificate; you add it to the pipeline |
| `MtlsForwardedHeaderOptions` | `HeaderName`, `TrustedNetworks`, `AddTrustedNetwork`, `AddTrustedProxy` |

### Logging

| Event id | Level | Event |
| --- | --- | --- |
| 13000 | Information | Client certificate accepted (thumbprint, subject) |
| 13001 | Warning | Client certificate rejected (thumbprint, subject, reason) |
| 13003 | Warning | Forwarded-header path has no `TrustedNetworks` allow-list for header `{HeaderName}` |

Raw certificate bytes are never logged.

## Testing

Build certificates with [`SharedKernel.Security.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Hosting/Security/SharedKernel.Security.Testing/README.md)'s
`MtlsTestCertificateBuilder` (`WithSubjectName`, `AsSelfSigned`, `AsChainedFromEphemeralCa`, `AsRevoked`, `Build()`).
For the forwarded-header path, send the certificate's Base64 DER in the configured header through a
`WebApplicationFactory<Program>` client; `TestServer` requests come from no remote IP, so leave `TrustedNetworks`
empty in that test host or assert the untrusted path.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Leave `TrustedNetworks` empty in production | Add the ingress subnet or proxy addresses | Any path that reaches the pod directly could forge the header |
| Call a CRL, OCSP or remote policy service in a validator used by Kestrel | Answer from an in-memory allow-list or cached trust decision | The validator blocks inside the TLS handshake |
| Place the middleware after `UseAuthentication()` | Add it first (`AtStart`) | The mTLS handler reads `Connection.ClientCertificate` |
| Forget to register `IMtlsCertificateValidator` | Call `AddMtlsAuthentication<TValidator>()` | Neither path fails at startup; the first handshake or request does |
| Let the ingress pass through a client-supplied certificate header | Configure the ingress to overwrite the header | The header must only ever hold what the ingress verified |

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) ·
[ServiceDefaults domain](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Hosting/ServiceDefaults/README.md) ·
[MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
