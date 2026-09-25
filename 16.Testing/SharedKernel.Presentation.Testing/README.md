# SharedKernel.Presentation.Testing

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![Test projects only](https://img.shields.io/badge/use-test%20projects%20only-orange)
![Test framework: any](https://img.shields.io/badge/test%20framework-any-informational)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)

Test helpers for the SharedKernel presentation packages: a gRPC `ServerCallContext` that carries an `HttpContext` (services and endpoint metadata) for server interceptor tests, `GraphQLTestExecutorFactory` (a HotChocolate request executor with the platform paging defaults) and `FakeHttpContextAccessor`.

## Install

Reference it from a **test project only**; an architecture test fails any production project that references a
`SharedKernel.*.Testing` package.

```xml
<PackageReference Include="SharedKernel.Presentation.Testing" />
```

## Contents

- **Namespaces:** `SharedKernel.Testing.Communication`, `SharedKernel.Testing.Grpc`
- **Types:** `FakeHttpContextAccessor`, `GraphQLTestExecutorFactory`, `TestServerCallContext`

## Dependencies

References `SharedKernel.Presentation.Grpc` (and through it ASP.NET Core), HotChocolate and `Grpc.Core.Testing`. No test framework is referenced: the doubles work under xUnit, NUnit or MSTest. The lightweight
`SharedKernel.Testing` package supplies the shared basics (`FakeClock`, `InMemoryLogger`, `TestRequestContext`,
fakers and assertions).
