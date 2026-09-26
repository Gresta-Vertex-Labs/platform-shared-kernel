# SharedKernel.ServiceDefaults.Security.Mtls

Mutual-TLS host composition: accept client certificates either negotiated directly by Kestrel or
forwarded by a TLS-terminating proxy. One of the `SharedKernel.ServiceDefaults.*` integration packages.
**Tier:** Host.

Neither path validates a certificate itself. Both delegate the accept or reject decision to
`SharedKernel.Security.Mtls`'s `IMtlsCertificateValidator`, which must already be registered — typically
through `AddMtlsAuthentication<TValidator>()`. Without it, the Kestrel path throws at the first TLS
handshake and the forwarded-header path at the first request; neither fails at startup.

## Usage

```xml
<PackageReference Include="SharedKernel.ServiceDefaults" />
<PackageReference Include="SharedKernel.ServiceDefaults.Security.Mtls" />
```

**TLS terminates at Kestrel:**

```csharp
using SharedKernel.ServiceDefaults.Security;

builder.AddServiceDefaults();
builder.AddMtlsClientCertificate(ClientCertificateMode.RequireCertificate); // default: AllowCertificate
```

No middleware is needed; this configures `KestrelServerOptions.ConfigureHttpsDefaults`.

**TLS terminates at an ingress or gateway that forwards the certificate as a header:**

```csharp
builder.AddMtlsForwardedHeaderCertificate(o =>
{
    o.HeaderName = "ssl-client-cert";                      // required — there is no default
    o.AddTrustedNetwork(IPNetwork.Parse("10.0.0.0/16"));    // the ingress-controller subnet
    o.AddTrustedProxy(IPAddress.Parse("10.0.5.7"));         // a single known proxy hop
});

var app = builder.Build();
app.UseMiddleware<MtlsForwardedHeaderMiddleware>();        // you wire the middleware yourself
```

`HeaderName` has no default because nginx-ingress, Envoy, Istio, and HAProxy each use a different
header name and encoding.

## Rules

| Rule | Why |
| --- | --- |
| Configure `TrustedNetworks` in production | Left empty, **any** network path that reaches the host directly — a misconfigured `NetworkPolicy`, a multi-hop mesh, a debug port, a compromised sidecar — can forge the certificate header exactly as the real ingress would. A one-time startup warning (EventId `13003`) fires whenever it is empty. |
| Keep a validator used on the Kestrel path fast | Kestrel's certificate callback is synchronous while `IMtlsCertificateValidator` is async, so the call blocks inside the TLS handshake. Resolve from an in-memory allow-list or a cached trust decision; never make a CRL, OCSP, or remote policy call there. |
| A request from outside `TrustedNetworks` is ignored, not validated | The header is never decoded and `HttpContext.Connection.ClientCertificate` is never set, even if the certificate itself would pass. |

## Logging

| EventId | Level | Event |
| --- | --- | --- |
| `13000` | Information | Client certificate accepted — thumbprint and subject |
| `13001` | Warning | Client certificate rejected — thumbprint, subject, and reason |
| `13003` | Warning | Forwarded-header path configured with no `TrustedNetworks` |

Raw certificate bytes are never logged.

## Why a separate package

It brings `SharedKernel.Security.Mtls`, which a service without mutual TLS has no use for. The types live
in the `SharedKernel.ServiceDefaults.Security` namespace.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel). See the
[SharedKernel.ServiceDefaults README](../SharedKernel.ServiceDefaults/README.md) for the composition base
and the full list of integration packages.
