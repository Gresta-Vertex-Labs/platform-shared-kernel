# SharedKernel.Reporting.Testing

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![Test projects only](https://img.shields.io/badge/use-test%20projects%20only-orange)
![Test framework: any](https://img.shields.io/badge/test%20framework-any-informational)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)

`InMemoryReportExporter<TRow>` implements `SharedKernel.Reporting.Abstractions`' exporter contract in memory: it streams the rows, records the report and delivers it to a storage store, so export code can be tested without a real CSV, spreadsheet or PDF provider.

## Install

Reference it from a **test project only**; an architecture test fails any production project that references a
`SharedKernel.*.Testing` package.

```xml
<PackageReference Include="SharedKernel.Reporting.Testing" />
```

## Contents

- **Namespaces:** `SharedKernel.Testing.Reporting`
- **Types:** `InMemoryReportExporter`

## Dependencies

References `SharedKernel.Reporting.Abstractions`, `SharedKernel.Storage.Abstractions` and `SharedKernel.Testing` (`FakeClock`). No test framework is referenced: the doubles work under xUnit, NUnit or MSTest. The lightweight
`SharedKernel.Testing` package supplies the shared basics (`FakeClock`, `InMemoryLogger`, `TestRequestContext`,
fakers and assertions).
