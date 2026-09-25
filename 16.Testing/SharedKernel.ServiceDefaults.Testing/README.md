# SharedKernel.ServiceDefaults.Testing

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![Test projects only](https://img.shields.io/badge/use-test%20projects%20only-orange)
![Test framework: any](https://img.shields.io/badge/test%20framework-any-informational)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)

Test helpers for the SharedKernel host packages: `InMemoryTenantCatalog` (an `ITenantCatalog`), `FakeTenantResolutionStrategy` and `HealthCheckAssertionExtensions` for asserting health-check registrations and results.

## Install

Reference it from a **test project only**; an architecture test fails any production project that references a
`SharedKernel.*.Testing` package.

```xml
<PackageReference Include="SharedKernel.ServiceDefaults.Testing" />
```

## Contents

- **Namespaces:** `SharedKernel.Testing.ServiceDefaults`
- **Types:** `FakeTenantResolutionStrategy`, `HealthCheckAssertionExtensions`, `InMemoryTenantCatalog`

## Dependencies

References `SharedKernel.MultiTenancy` (and through it ASP.NET Core) and the health-check abstractions. No test framework is referenced: the doubles work under xUnit, NUnit or MSTest. The lightweight
`SharedKernel.Testing` package supplies the shared basics (`FakeClock`, `InMemoryLogger`, `TestRequestContext`,
fakers and assertions).
