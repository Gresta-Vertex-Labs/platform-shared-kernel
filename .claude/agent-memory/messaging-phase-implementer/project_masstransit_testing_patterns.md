---
name: masstransit-9x-testing-patterns
description: MassTransit 9.x testing patterns discovered during SK.07.Tests implementation — covers type naming, harness API, SQLite limitations, ServiceProvider disposal
metadata:
  type: project
---

# MassTransit 9.x TestHarness Patterns (Discovered SK.07.Tests)

> WO-086 (2026-09): recorded on MassTransit 9.1.2; the platform is pinned to 8.5.x (P-560) — re-verify API claims. `RequestAsync` (and its `RequestAsyncTimeoutTests`) was removed by P-560; outbox tests now live in `SharedKernel.Messaging.MassTransit.EfCore.Tests`, transport tests in `.RabbitMq.Tests`/`.AzureServiceBus.Tests`; the repo's own harness builder is `16.Testing/SharedKernel.Testing.Internal`'s `TestHarnessFactory`.

## Package
`MassTransit.TestFramework` 9.1.2 — NOT `MassTransit.Testing` (renamed in 9.x).
Use `AddMassTransitTestHarness()`, resolve `ITestHarness` from DI.

## `file` modifier breaks type matching
C# `file` types get mangled CLR names like `<ConsumerBaseTests>F15BB...RecordingConsumer` with `<` characters.
MassTransit `KebabCaseEndpointNameFormatter` splits on `<`; harness `GetConsumerHarness<T>()` and
`Consumed.Select<T>()` fail to match. **Always use `internal` + unique names per file.**

## Consumer suffix stripping
`KebabCaseEndpointNameFormatter` in MassTransit 9.x strips the `Consumer` suffix by default.
- `OrderPlacedConsumer` → `order-placed` (not `order-placed-consumer`)
- `Event` suffix is NOT stripped: `OrderPlacedEventConsumer` → `order-placed-event`

## `IConsumerTestHarness<T>` API changes in 9.x
`.Consumer.InputAddress` does NOT exist. To test endpoint naming, call
`new KebabCaseEndpointNameFormatter(prefix, false).Consumer<TConsumer>()` directly.

## `await using` for ServiceProvider
`MassTransit.UsageTracking.UsageTracker` only implements `IAsyncDisposable` not `IDisposable`.
`using var sp` throws at synchronous dispose. Always: `await using var sp = services.BuildServiceProvider(...)`.
Test methods must be `async Task`.

## OptionsValidationException vs InvalidOperationException
`MessagingOptionsValidator` fires `IValidateOptions<MessagingOptions>` which throws `OptionsValidationException`,
not `InvalidOperationException`. Guard test assertions must be:
`.Throw<Exception>().Where(e => e.Message.Contains("ServiceName"))` — not `.Throw<InvalidOperationException>()`.

## Retry count not per-attempt in Consumed
`harness.Consumed.Select<T>()` does NOT expose individual retry attempts — only successful consumptions.
Retry tests should assert: `harness.Consumed.Any<T>()` = true (eventual success) AND
`harness.Published.Any<Fault<T>>()` = false (no dead-letter). Cannot count retry attempts via harness.

## Outbox row timing
Rows are written DURING `SaveChangesAsync` via `OutboxSaveChangesObserver` — not before.
Test flow: `publisher.PublishAsync(evt)` → `db.SaveChangesAsync()` → then assert row count via raw SQL.
Assertion must be AFTER SaveChangesAsync.

## SQLite outbox delivery worker limitation
The outbox delivery worker uses `RepeatableRead` isolation requiring nested transactions.
SQLite does not support nested transactions — starting TestHarness (which starts the delivery worker)
will throw `SqliteException: cannot start a transaction within a transaction`.
Fix: register with `AddMassTransit + UsingInMemory` (no TestHarness) for row-insertion tests.
Use TestHarness WITHOUT outbox for IEventPublisher publish tests.

## SQLite in-memory database persistence
SQLite in-memory DB is dropped when the connection closes.
To persist across multiple service scopes: open `SqliteConnection("Data Source=:memory:")`,
keep it open for the test lifetime (implement `IDisposable`), pass to `UseSqlite(connection)`.

## Tests bypassing MessagingBusBuilder.Build() must register routing deps

`MassTransitMessageBus` constructor now requires `IReadOnlyDictionary<Type, string>` (route map)
and `ConventionSendEndpointResolver`. Tests that register `IMessageBus, MassTransitMessageBus` directly
(e.g. `RequestAsyncTimeoutTests`) must also add:

```csharp
services.AddSingleton<IReadOnlyDictionary<Type, string>>(
    new ReadOnlyDictionary<Type, string>(new Dictionary<Type, string>()));
services.AddScoped<ConventionSendEndpointResolver>();
```

Tests going through `.Build()` get these registrations automatically.

## KebabCaseEndpointNameFormatter.SanitizeName is an instance method

Call via `KebabCaseEndpointNameFormatter.Instance.SanitizeName(typeof(T).Name)`.
NOT a static method — `KebabCaseEndpointNameFormatter.SanitizeName(...)` causes CS0120.

**Why:** Phase SK.07.Tests implemented 2026-06-08; SK.07.Routing implemented 2026-06-09; 79 MassTransit tests pass.
**How to apply:** Apply all patterns above in any future messaging test work.

## Locale-safe decimal assertions (kept from the retired RoutingSlip notes)
Never compare against a literal decimal string like `"49.99"` — `decimal.ToString()` is locale-dependent (Turkish locale produces `"49,99"`). Use `.ToString(CultureInfo.InvariantCulture)` on both sides.
