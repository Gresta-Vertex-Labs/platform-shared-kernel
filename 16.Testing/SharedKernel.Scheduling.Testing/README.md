# SharedKernel.Scheduling.Testing

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![Test projects only](https://img.shields.io/badge/use-test%20projects%20only-orange)
![Test framework: any](https://img.shields.io/badge/test%20framework-any-informational)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)

`InMemoryScheduledJobRegistry` records job registrations made through `SharedKernel.Scheduling`'s `IScheduledJobRegistry`, with their schedules, misfire and overlap policies, so registration code can be asserted without running the scheduler.

## Install

Reference it from a **test project only**; an architecture test fails any production project that references a
`SharedKernel.*.Testing` package.

```xml
<PackageReference Include="SharedKernel.Scheduling.Testing" />
```

## Contents

- **Namespaces:** `SharedKernel.Testing.Scheduling`
- **Types:** `InMemoryScheduledJobRegistry`

## Dependencies

References `SharedKernel.Scheduling`. No test framework is referenced: the doubles work under xUnit, NUnit or MSTest. The lightweight
`SharedKernel.Testing` package supplies the shared basics (`FakeClock`, `InMemoryLogger`, `TestRequestContext`,
fakers and assertions).
