# SharedKernel.Reporting.Gotenberg

HTML-to-PDF for `SharedKernel.Reporting`, rendered by [Gotenberg](https://gotenberg.dev) (MIT) — a stateless Docker
service running headless Chromium. You send an HTML string; you get a PDF in object storage (with a presigned link) or
in any stream. The contract, `IHtmlToPdfConverter`, is in
[`SharedKernel.Reporting.Abstractions`](../SharedKernel.Reporting.Abstractions/README.md); application code depends
only on it.

```xml
<PackageReference Include="SharedKernel.Reporting.Gotenberg" />
```

Versions come from the consumer's single `SharedKernelVersion`. **Tier: Adapter.** No vendor SDK: plain HTTP through
`IHttpClientFactory` and `Microsoft.Extensions.Http.Resilience`.

## Register

```csharp
builder.Services.AddSharedKernelStorage().AddS3(builder.Configuration).AddStore("invoices");
builder.Services.AddSharedKernelReporting().AddGotenberg(builder.Configuration);
builder.Services.AddHealthChecks().AddSharedKernelReadiness();   // includes the "gotenberg" probe
builder.WithReportingTelemetry();
```

```json
"SharedKernel": { "Reporting": { "Gotenberg": { "BaseUrl": "http://gotenberg:3000" } } }
```

| Setting | Default | |
|---|---|---|
| `BaseUrl` | — (required) | The Gotenberg address. |
| `Timeout` | `00:01:00` | One attempt; keep it near Gotenberg's `--api-timeout` (30 s by default). |
| `MaxRetryAttempts` | `2` | Retries on 5xx, timeouts and dropped connections. A conversion has no side effects. |
| `Username` / `Password` | — | For a Gotenberg started with `--api-enable-basic-auth`. From a secret store. |

## Convert

```csharp
Result<PdfDocumentOutcome> stored = await converter.ConvertAsync(
    html,                                                   // a complete document; set the PDF title with <title>
    new ReportDestination
    {
        Store = "invoices",
        TenantId = requestContext.TenantId,
        Key = $"{invoice.Number}.pdf",
        DownloadFileName = $"Invoice {invoice.Number}.pdf",
        Condition = WriteCondition.IfNotExists,             // an issued invoice is never overwritten
        PresignedDownloadUrlExpiry = TimeSpan.FromMinutes(15),
    },
    new HtmlToPdfOptions
    {
        PageSize = PdfPageSize.A4,
        Margins = new PdfMargins(Top: 15, Right: 12, Bottom: 18, Left: 12),
        FooterHtml = HtmlToPdfOptions.PageNumberFooter,     // "3 / 12"
        Assets = [new HtmlAsset("logo.png", logoBytes)],    // <img src="logo.png">
    },
    ct);
```

`ConvertToStreamAsync` writes to any stream instead (an HTTP response body, a `MemoryStream` for an e-mail attachment).
The PDF streams from Gotenberg to the destination; it is never held in memory.

Failures are `Result` values: `reporting.invalid_request` (empty HTML, invalid options), `reporting.conversion_failed`
(Gotenberg rejected the document), `reporting.converter_unavailable` (unreachable, 5xx, bad credentials or `BaseUrl`),
`reporting.conversion_timeout`; storage failures keep their `storage.*` codes.

## Deploy Gotenberg

Run it as its own Deployment and Service (`gotenberg/gotenberg:8`, port 3000), scaled on CPU. Chromium stays out of
your service's image and process.

**The HTML runs in a browser, so treat it as code.**

- HTML-encode every value that came from a user before it goes into the markup.
- Stop the converter reaching anything but its own files: start Gotenberg with
  `--chromium-allow-list=^file:///tmp/.*` (assets and header/footer are served from there), and give its pods a
  NetworkPolicy that denies egress. Otherwise a document can make Chromium fetch internal addresses (SSRF).
- Keep Gotenberg internal to the cluster, or enable basic authentication.

## Tests

`SharedKernel.Reporting.Testing` has `InMemoryHtmlToPdfConverter` (records every document, writes a placeholder PDF)
and `services.AddInMemoryReporting()`.
