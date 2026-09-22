# samples

Runnable services built on the SharedKernel packages.

Unlike the per-domain `consumer-verify` harnesses — which prove a single package's
dependency graph resolves — a sample is a working service that composes several domains the
way a real microservice would.

| Sample | Domains exercised | External infrastructure |
|---|---|---|
| [`OrderApi`](OrderApi/) | `01.Core`, `03.Domain`, `05.Application`, `13.ServiceDefaults`, `14.Presentation` | none |
| [`BillingApi`](BillingApi/) | `06.Persistence` (all six packages + `SharedKernel.Persistence.Testing`), `01.Core`, `03.Domain`, `05.Application`, `12.Security`, `13.ServiceDefaults`, `14.Presentation` | PostgreSQL (Docker Compose, or Testcontainers in its tests) |

## Why these use PackageReference

Every sample resolves `SharedKernel.*` from the local NuGet feed (`nupkgs/`) via
`PackageReference`, never `ProjectReference`. The point is to prove the **packed** packages
work for a consumer who has only the published artifacts — a `ProjectReference` would bypass
exactly the thing under test.

That has two consequences, both deliberate:

1. Samples are **excluded from `Platform.SharedKernel.slnx`**. They cannot build until the
   packages they consume have been packed, so a solution-wide build would fail on a clean
   checkout.
2. CI builds and runs them in the `packaging-verify` job, after `dotnet pack` — the same
   pattern the `consumer-verify` harnesses use.

To run one locally, pack first:

```bash
dotnet pack Platform.SharedKernel.slnx -c Release
dotnet run --project samples/OrderApi
```
