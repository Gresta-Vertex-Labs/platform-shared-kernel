# SharedKernel.FeatureManagement.Testing

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![Test projects only](https://img.shields.io/badge/use-test%20projects%20only-orange)
![Test framework: any](https://img.shields.io/badge/test%20framework-any-informational)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)

`FakeFeatureClient` is an OpenFeature `IFeatureClient` for tests of code built on `SharedKernel.FeatureManagement`: set a flag with `SetEnabled`, `Set` (a value or a function of the evaluation context) or `SetObject`; an unset flag returns its default with `FlagNotFound`, as in production. `AddFakeFeatureFlags(...)` replaces the real registration.

## Install

Reference it from a **test project only**; an architecture test fails any production project that references a
`SharedKernel.*.Testing` package.

```xml
<PackageReference Include="SharedKernel.FeatureManagement.Testing" />
```

## Contents

- **Namespaces:** `SharedKernel.Testing.FeatureManagement`
- **Types:** `FakeFeatureClient`, `FakeFeatureClientServiceCollectionExtensions`

## Dependencies

References `SharedKernel.FeatureManagement` (and through it OpenFeature). No test framework is referenced: the doubles work under xUnit, NUnit or MSTest. The lightweight
`SharedKernel.Testing` package supplies the shared basics (`FakeClock`, `InMemoryLogger`, `TestRequestContext`,
fakers and assertions).
