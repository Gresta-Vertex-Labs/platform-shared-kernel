# SharedKernel.Workflows.Testing

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![Test projects only](https://img.shields.io/badge/use-test%20projects%20only-orange)
![Test framework: any](https://img.shields.io/badge/test%20framework-any-informational)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)

`InMemoryWorkflowDispatcher` and `InMemoryWorkflowHandle` implement the `SharedKernel.Workflows.Temporal` dispatch surface in memory: starts, signals, queries, cancellations and terminations are recorded per tenant, and a test completes or fails a workflow to drive the caller. Register with `AddInMemoryWorkflowDispatcher()`.

## Install

Reference it from a **test project only**; an architecture test fails any production project that references a
`SharedKernel.*.Testing` package.

```xml
<PackageReference Include="SharedKernel.Workflows.Testing" />
```

## Contents

- **Namespaces:** `SharedKernel.Testing.Workflows`
- **Types:** `InMemoryWorkflowDispatcher`, `InMemoryWorkflowHandle`, `InMemoryWorkflowStartRecord`, `WorkflowLifecycleStatus`, `WorkflowServiceCollectionExtensions`

## Dependencies

References `SharedKernel.Workflows.Temporal` (the dispatch contracts ship there; there is no separate abstractions package). No test framework is referenced: the doubles work under xUnit, NUnit or MSTest. The lightweight
`SharedKernel.Testing` package supplies the shared basics (`FakeClock`, `InMemoryLogger`, `TestRequestContext`,
fakers and assertions).
