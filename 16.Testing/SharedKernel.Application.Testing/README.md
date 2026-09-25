# SharedKernel.Application.Testing

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![Test projects only](https://img.shields.io/badge/use-test%20projects%20only-orange)
![Test framework: any](https://img.shields.io/badge/test%20framework-any-informational)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)

Run a command or query through the real SharedKernel application pipeline in a unit test. `ApplicationPipelineTestHarness` registers the behaviors you choose, optionally the MediatR adapter over your handler assembly, and captures the pipeline's activities and metric measurements; `AddFakeApplicationBehaviorServices()` registers the fake unit of work, request context and idempotency store the behaviors need.

## Install

Reference it from a **test project only**; an architecture test fails any production project that references a
`SharedKernel.*.Testing` package.

```xml
<PackageReference Include="SharedKernel.Application.Testing" />
```

## Contents

- **Namespaces:** `SharedKernel.Testing.Application`
- **Types:** `ApplicationPipelineTestHarness`, `ApplicationServiceCollectionExtensions`

## Dependencies

References `SharedKernel.Application.Pipeline`, `SharedKernel.Application.Mediator.MediatR`, `SharedKernel.Persistence.Testing` (the fake unit of work) and `SharedKernel.Idempotency.Testing`. No test framework is referenced: the doubles work under xUnit, NUnit or MSTest. The lightweight
`SharedKernel.Testing` package supplies the shared basics (`FakeClock`, `InMemoryLogger`, `TestRequestContext`,
fakers and assertions).
