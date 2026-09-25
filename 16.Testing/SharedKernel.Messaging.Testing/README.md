# SharedKernel.Messaging.Testing

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![Test projects only](https://img.shields.io/badge/use-test%20projects%20only-orange)
![Test framework: any](https://img.shields.io/badge/test%20framework-any-informational)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)

`InMemoryMessageBus` and `InMemoryEventPublisher` record every publish and send made through `SharedKernel.Messaging.Abstractions`, with assertions (`ShouldHavePublished<T>()`, `ShouldHaveSent<T>()`, `ShouldHavePublishedOnce<T>()`, `ShouldNotHavePublished<T>()`), so a handler that publishes can be tested without a broker. Register with `AddInMemoryMessageBus()` / `AddInMemoryEventPublisher()`.

## Install

Reference it from a **test project only**; an architecture test fails any production project that references a
`SharedKernel.*.Testing` package.

```xml
<PackageReference Include="SharedKernel.Messaging.Testing" />
```

## Contents

- **Namespaces:** `SharedKernel.Testing.Messaging`
- **Types:** `InMemoryEventPublisher`, `InMemoryMessageBus`, `MessagingServiceCollectionExtensions`

## Dependencies

References `SharedKernel.Messaging.Abstractions` and `SharedKernel.Contracts`; no MassTransit. No test framework is referenced: the doubles work under xUnit, NUnit or MSTest. The lightweight
`SharedKernel.Testing` package supplies the shared basics (`FakeClock`, `InMemoryLogger`, `TestRequestContext`,
fakers and assertions).
