# SharedKernel.Storage.Testing

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![Test projects only](https://img.shields.io/badge/use-test%20projects%20only-orange)
![Test framework: any](https://img.shields.io/badge/test%20framework-any-informational)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)

An in-memory object store for `SharedKernel.Storage.Abstractions`, registered like a real provider: `services.AddSharedKernelStorage().AddInMemoryStore(name)` or `.AddInMemoryTenantStore(name)`, so named stores, tenant views, request validation, conditional writes, checksums and presigned URLs behave as in production. `InMemoryStorage.CreateFactory(...)` builds one without a container.

## Install

Reference it from a **test project only**; an architecture test fails any production project that references a
`SharedKernel.*.Testing` package.

```xml
<PackageReference Include="SharedKernel.Storage.Testing" />
```

## Contents

- **Namespaces:** `SharedKernel.Testing.Storage`
- **Types:** `InMemoryFileStorage`, `InMemoryFileStorageOptions`, `InMemoryStorage`, `InMemoryStorageBuilderExtensions`

## Dependencies

References `SharedKernel.Storage.Abstractions` only; no cloud SDK. No test framework is referenced: the doubles work under xUnit, NUnit or MSTest. The lightweight
`SharedKernel.Testing` package supplies the shared basics (`FakeClock`, `InMemoryLogger`, `TestRequestContext`,
fakers and assertions).
