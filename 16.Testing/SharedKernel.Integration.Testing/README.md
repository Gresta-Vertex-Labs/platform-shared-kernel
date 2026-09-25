# SharedKernel.Integration.Testing

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![Test projects only](https://img.shields.io/badge/use-test%20projects%20only-orange)
![Test framework: any](https://img.shields.io/badge/test%20framework-any-informational)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)

In-memory doubles for the SharedKernel integration packages: `InMemoryWebhookDispatcher` and `InMemoryWebhookDeliveryObserver` for webhooks, `InMemoryNotificationSender` and `InMemoryNotificationDeliveryObserver` for email and SMS, with DI helpers that replace the real registrations.

## Install

Reference it from a **test project only**; an architecture test fails any production project that references a
`SharedKernel.*.Testing` package.

```xml
<PackageReference Include="SharedKernel.Integration.Testing" />
```

## Contents

- **Namespaces:** `SharedKernel.Testing.Integration`, `SharedKernel.Testing.Notifications`
- **Types:** `InMemoryNotificationDeliveryObserver`, `InMemoryNotificationSender`, `InMemoryWebhookDeliveryObserver`, `InMemoryWebhookDispatcher`, `IntegrationServiceCollectionExtensions`, `NotificationsServiceCollectionExtensions`

## Dependencies

References `SharedKernel.Integration.Webhooks`, `SharedKernel.Integration.Notifications.Abstractions` and `SharedKernel.Contracts`. No test framework is referenced: the doubles work under xUnit, NUnit or MSTest. The lightweight
`SharedKernel.Testing` package supplies the shared basics (`FakeClock`, `InMemoryLogger`, `TestRequestContext`,
fakers and assertions).
