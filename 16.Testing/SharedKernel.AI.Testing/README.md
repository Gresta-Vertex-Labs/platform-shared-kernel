# SharedKernel.AI.Testing

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![Test projects only](https://img.shields.io/badge/use-test%20projects%20only-orange)
![Test framework: any](https://img.shields.io/badge/test%20framework-any-informational)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)

In-memory doubles for `SharedKernel.AI.Abstractions`: a deterministic, hash-derived `InMemoryEmbeddingGenerator` (no model or network), `InMemoryVectorCollection` and `InMemoryVectorCollectionProvisioner` with tenant scoping, provider descriptors, and `InMemorySemanticKernel` that returns scripted completions.

## Install

Reference it from a **test project only**; an architecture test fails any production project that references a
`SharedKernel.*.Testing` package.

```xml
<PackageReference Include="SharedKernel.AI.Testing" />
```

## Contents

- **Namespaces:** `SharedKernel.Testing.Intelligence`
- **Types:** `InMemoryCompletionProviderDescriptor`, `InMemoryEmbeddingGenerator`, `InMemorySemanticKernel`, `InMemoryVectorCollection`, `InMemoryVectorCollectionProvisioner`, `InMemoryVectorProviderDescriptor`, `IntelligenceServiceCollectionExtensions`

## Dependencies

References `SharedKernel.AI.Abstractions` only; no Qdrant or Semantic Kernel. No test framework is referenced: the doubles work under xUnit, NUnit or MSTest. The lightweight
`SharedKernel.Testing` package supplies the shared basics (`FakeClock`, `InMemoryLogger`, `TestRequestContext`,
fakers and assertions).
