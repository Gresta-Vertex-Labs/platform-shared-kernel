# SharedKernel.Search.Testing

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![Test projects only](https://img.shields.io/badge/use-test%20projects%20only-orange)
![Test framework: any](https://img.shields.io/badge/test%20framework-any-informational)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)

In-memory doubles for `SharedKernel.Search.Abstractions`: `InMemorySearchIndex<TDocument>`, `InMemorySearchIndexProvisioner` and `InMemorySearchProviderDescriptor` evaluate the neutral filter AST, paging, counts and the mandatory `TenantScope` without a search engine.

## Install

Reference it from a **test project only**; an architecture test fails any production project that references a
`SharedKernel.*.Testing` package.

```xml
<PackageReference Include="SharedKernel.Search.Testing" />
```

## Contents

- **Namespaces:** `SharedKernel.Testing.Search`
- **Types:** `InMemorySearchIndex`, `InMemorySearchIndexProvisioner`, `InMemorySearchProviderDescriptor`, `SearchServiceCollectionExtensions`

## Dependencies

References `SharedKernel.Search.Abstractions` only. No test framework is referenced: the doubles work under xUnit, NUnit or MSTest. The lightweight
`SharedKernel.Testing` package supplies the shared basics (`FakeClock`, `InMemoryLogger`, `TestRequestContext`,
fakers and assertions).
