# SharedKernel.Reporting.Abstractions

Streaming, memory-bounded report/data export contracts for Platform.SharedKernel microservices. Zero third-party NuGet dependencies — references only `SharedKernel.Primitives`, `SharedKernel.Storage.Abstractions` and `Microsoft.Extensions.Logging.Abstractions`.

Implemented by [`SharedKernel.Reporting.Csv`](../SharedKernel.Reporting.Csv/README.md), [`SharedKernel.Reporting.Spreadsheet`](../SharedKernel.Reporting.Spreadsheet/README.md), and [`SharedKernel.Reporting.Pdf`](../SharedKernel.Reporting.Pdf/README.md).

```xml
<PackageReference Include="SharedKernel.Reporting.Abstractions" />
```

Versions come from the consumer's single `SharedKernelVersion`. **Tier: Abstractions.** For unit tests, `SharedKernel.Reporting.Testing` provides `InMemoryReportExporter<TRow>`, which records every export instead of writing a file.

## The contract

```csharp
public interface IReportExporter<TRow>
{
    Task<Result<ReportExportOutcome>> ExportAsync(
        IAsyncEnumerable<TRow> rows,
        ReportDefinition<TRow> definition,
        ReportDestination destination,
        CancellationToken cancellationToken);

    Task<Result> ExportToStreamAsync(
        IAsyncEnumerable<TRow> rows,
        ReportDefinition<TRow> definition,
        Stream destination,
        CancellationToken cancellationToken);
}
```

Both members accept `IAsyncEnumerable<TRow>` — **never** `IEnumerable<TRow>` or `List<TRow>`, and no such overload will ever be added. `ExportAsync` is the primary, storage-delivered path (into a named `IFileStorage` store, with an optional presigned download request created by that store). `ExportToStreamAsync` is a small-output/direct-stream convenience path — never the *only* way out of a provider.

If you have an in-memory collection, convert it yourself: `myList.ToAsyncEnumerable()` (`System.Linq.Async` or a one-line adapter). That is your call to make, not this contract's to weaken.

## The column/definition model

```csharp
var definition = new ReportDefinition<Invoice>
{
    Culture = CultureInfo.GetCultureInfo("de-DE"),
    Title = "Q1 Invoices",
    Columns =
    [
        new ReportColumn<Invoice> { Header = "Id", Ordinal = 0, ValueSelector = i => i.Id },
        new ReportColumn<Invoice> { Header = "Amount", Ordinal = 1, ValueSelector = i => i.Amount },
    ],
};
```

- `Ordinal` is the column's explicit output position — not its position in the `Columns` list. Every provider renders columns sorted by `Ordinal`.
- A `null` from `ValueSelector`, or a `null` from an optional `Formatter`, always means a blank cell — never the literal text `"null"`.
- `Culture` defaults to `CultureInfo.InvariantCulture` and drives `ReportValueFormatting.Format` — the default formatter every provider applies when a column supplies no `Formatter`. This is BCL culture formatting (numbers/dates/currency) — never a translation catalog. A translated column *header* is the caller's job, resolved before it reaches `Header`.

## Delivery

```csharp
var destination = new ReportDestination
{
    Store = "exports",                                   // a store registered with AddSharedKernelStorage()
    TenantId = tenantId,                                 // only for a tenant store; from the authenticated request
    Key = $"invoices/{DateOnly.FromDateTime(DateTime.UtcNow)}.csv",
    PresignedDownloadUrlExpiry = TimeSpan.FromHours(1), // omit for no presigned URL
};

var result = await exporter.ExportAsync(rows, definition, destination, cancellationToken);
if (result.IsSuccess)
{
    FileReference stored = result.Value.StoredFile;    // store, tenant and key — persist this
    PresignedRequest? link = result.Value.DownloadUrl; // Url, Method, Headers, ExpiresAt; only when PresignedDownloadUrlExpiry was set
    long rowCount = result.Value.RowCount;             // counted for free while streaming
}
```

- `Store` names a store the host registered; an unknown store, or a `TenantId` that does not match the store's tenancy (set for a shared store, missing for a tenant store), is a configuration error and throws.
- For a tenant store the object lands under that tenant's own prefix, and `StoredFile` carries the tenant. Open it later with `IFileStorageFactory.Open(storedFile)`.
- `PresignedDownloadUrlExpiry` may not exceed the store's `MaxPresignExpiry`; a longer one fails the export with `storage.expiry_too_long` after the object has been stored.
- Every storage failure (`storage.already_exists`, `storage.unavailable`, …) comes back as the failed `Result` with the storage error code unchanged.

`ExportAsync` is composed internally from `StorageStreamingWriter` — a `System.IO.Pipelines.Pipe`-based primitive that runs `IFileStorage.UploadAsync` concurrently against a provider's own row-to-bytes encoder, so bytes reach storage as they are produced rather than after the whole output is buffered. This package exposes no DI registration of its own — each provider (`.Csv`/`.Spreadsheet`/`.Pdf`) owns its own `AddXReportExporter<TRow>(IConfiguration)` extension, since only a concrete provider knows its own encoding.

## Out of scope, by design

| Concern | Lives in |
|---|---|
| Querying/streaming rows out of a database | The caller. This domain never references `06.Persistence` or opens a connection. |
| PII classification and redaction | `01.Core/SharedKernel.DataPrivacy`, applied by the caller **before** rows reach an exporter. See the capitalized statement on `IReportExporter<TRow>`'s own XML docs — rows arrive already-redacted or they leave un-redacted; there is no safety net here. |
| Translated column headers | The caller, before the column reaches `ReportColumn<TRow>.Header`. This domain only formats by `CultureInfo`. |
| Running an export on a schedule | `19.Scheduling`/`17.Workflows` — composed in consumer code; nothing to register here. |
| Tenant provisioning | `13.ServiceDefaults`. |
| A readiness probe / `IHealthCheck` | Nowhere. This domain is stateless — no persistent connection to be ready or not ready. |

## Full host composition (foreshadowing `consumer-verify`)

This package never registers itself — a provider does. A real composition root wires storage plus whichever provider(s) it needs, then resolves through a real `IHost`:

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSharedKernelStorage()
    .AddS3(builder.Configuration)
    .AddStore("exports");
builder.Services.AddCsvReportExporter<Invoice>(builder.Configuration);
builder.Services.AddSpreadsheetReportExporter<Invoice>(builder.Configuration); // optional, composes freely
builder.Services.AddPdfReportExporter<Invoice>(builder.Configuration);        // optional, composes freely

var host = builder.Build();
await host.StartAsync(); // ValidateOnStart() runs here — a misconfigured provider fails now, not on first export

var csvExporter = host.Services.GetRequiredService<ICsvReportExporter<Invoice>>();
```

All three providers share one `StorageStreamingWriter` singleton (registered idempotently via `TryAddSingleton` in each provider's own DI extension) — registering more than one provider in the same host is a supported, tested composition, not an afterthought. See `20.Reporting/consumer-verify` for the full working harness this snippet foreshadows.

## Memory model, honestly, per provider

This domain's contract *shape* never allows a materializing overload. Whether a given *provider's own encoding* is itself O(1)-memory is a separate, provider-specific fact:

- **`.Csv`** genuinely is constant-memory end to end.
- **`.Spreadsheet`** (ClosedXML) and **`.Pdf`** (MigraDoc/PdfSharp) are **not** — both third-party libraries build their full in-memory document object model before writing a byte. This is documented in capitals in each provider's own XML docs and README — read them before choosing a provider for a large row count.
