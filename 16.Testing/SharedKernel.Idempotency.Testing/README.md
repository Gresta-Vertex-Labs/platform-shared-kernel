# SharedKernel.Idempotency.Testing

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![Test projects only](https://img.shields.io/badge/use-test%20projects%20only-orange)
![Test framework: any](https://img.shields.io/badge/test%20framework-any-informational)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)

`FakeIdempotencyStore` implements `SharedKernel.Idempotency.Abstractions`' atomic reserve / complete / release contract in memory, per purpose and tenant, with the same fingerprint and token rules as the Redis and EF Core stores. Register it with `AddFakeIdempotencyStore(purposes)`.

## Install

Reference it from a **test project only**; an architecture test fails any production project that references a
`SharedKernel.*.Testing` package.

```xml
<PackageReference Include="SharedKernel.Idempotency.Testing" />
```

## Contents

- **Namespaces:** `SharedKernel.Testing.Idempotency`
- **Types:** `FakeIdempotencyServiceCollectionExtensions`, `FakeIdempotencyStore`

## Dependencies

References `SharedKernel.Idempotency.Abstractions` only. No test framework is referenced: the doubles work under xUnit, NUnit or MSTest. The lightweight
`SharedKernel.Testing` package supplies the shared basics (`FakeClock`, `InMemoryLogger`, `TestRequestContext`,
fakers and assertions).
