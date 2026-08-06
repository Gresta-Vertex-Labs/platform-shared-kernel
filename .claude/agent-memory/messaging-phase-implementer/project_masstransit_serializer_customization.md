---
name: project_masstransit_serializer_customization
description: MassTransit 9.1.2 custom ISerializerFactory/IMessageSerializer/IMessageDeserializer registration pitfalls (ClearSerialization+AddDeserializer requirement), the real Deserialize(ReceiveContext) entry point, and Headers/BodyConsumeContext gotchas — discovered implementing SK.07.PayloadTransform (P-346/WO-054)
metadata:
  type: project
---

# MassTransit 9.1.2 Custom Serializer/Deserializer Wiring (SK.07.PayloadTransform, P-346/WO-054)

## The real API shapes (confirmed via `dotnet test`-based reflection probes, not XML docs alone)

```csharp
public interface IMessageSerializer
{
    ContentType ContentType { get; }
    MessageBody GetMessageBody<T>(SendContext<T> context) where T : class;
}

public interface IMessageDeserializer  // extends IProbeSite → also needs Probe(ProbeContext)
{
    ContentType ContentType { get; }
    ConsumeContext Deserialize(ReceiveContext receiveContext);
    SerializerContext Deserialize(MessageBody body, Headers headers, Uri? destinationAddress = null);
    MessageBody GetMessageBody(string text);
    void Probe(ProbeContext context);
}

public interface ISerializerFactory
{
    ContentType ContentType { get; }
    IMessageSerializer CreateSerializer();
    IMessageDeserializer CreateDeserializer();
}
```

`IMessageSerializer` does NOT extend `IProbeSite` (no `Probe` member) — only `IMessageDeserializer` does. Adding
`Probe` to a serializer decorator is a compile error (CS1061); omitting it from a deserializer decorator is
CS0535.

`Uri destinationAddress` on `Deserialize(MessageBody, Headers, Uri)` must be `Uri? destinationAddress = null`
(nullable, with a default) to satisfy the interface's own nullable annotation — CS8767 otherwise.

## `Deserialize(ReceiveContext)` is the REAL entry point, not the 3-arg overload

MassTransit's own shipped `SystemTextJsonMessageSerializer.Deserialize(ReceiveContext)` is implemented as:
```csharp
public ConsumeContext Deserialize(ReceiveContext receiveContext) =>
    new BodyConsumeContext(receiveContext, Deserialize(receiveContext.Body, receiveContext.TransportHeaders, receiveContext.InputAddress));
```
`MassTransit.Serialization.BodyConsumeContext` is a **public** sealed class with a **public**
`ctor(ReceiveContext, SerializerContext)`. A custom `IMessageDeserializer` MUST implement
`Deserialize(ReceiveContext)` this exact way (delegating to its own 3-arg overload, then wrapping in
`BodyConsumeContext`) — a decorator that only implements the 3-arg overload and leaves `Deserialize(ReceiveContext)`
unimplemented/wrong never gets invoked by the real receive pipeline at all, because that's the actual entry point
the pipeline calls.

## `ISerializerFactory` registration requires TWO calls, not one — `ClearSerialization()` + `AddSerializer()` + `AddDeserializer()`

`IBusFactoryConfigurator.AddSerializer(factory, isSerializer: true)` ALONE only changes which serializer
*produces* outgoing messages. MassTransit's own already-registered default deserializer for the same content
type remains active on the RECEIVE side — `MassTransit.Configuration.SerializationConfiguration`'s registration
is additive-by-content-type, not overwrite-by-content-type, and the bus's pre-existing default lives in a
separate configuration-chain source (`_source`) a bare `AddSerializer` call never touches.

**Symptom if you skip this**: publish-side transform works fine (bytes are genuinely transformed), but every
message faults on receive with `System.Runtime.Serialization.SerializationException: An error occured while
deserializing the message envelope` → `System.Text.Json.JsonException: '0x00' is an invalid start of a value`
— MassTransit's own UNTOUCHED default `SystemTextJsonMessageSerializer.Deserialize(ReceiveContext)` is still
running on receive, choking trying to JSON-parse your genuinely-compressed/encrypted bytes. `harness.Consumed`
stays empty AND `harness.Published.Any<Fault<T>>()` stays false too (a deserialization failure this early
can't even publish a typed `Fault<T>` since it never learns the message type) — the harness just silently
never delivers.

**The fix, both calls required, in this order, before `ConfigureEndpoints`:**
```csharp
busCfg.ClearSerialization();
busCfg.AddSerializer(factory, isSerializer: true);
busCfg.AddDeserializer(factory, isDefault: true);
```
`ClearSerialization()` + `AddSerializer` ALONE (without the explicit `AddDeserializer(..., isDefault: true)`)
produces a DIFFERENT error: `MassTransit.ConfigurationException: No default content type specified and more
than one deserializer was configured` — thrown from `SerializationConfiguration.Validate()`/`CreateCollection()`
at bus-build time (`ContainerTestHarness.Start()` in tests). `AddSerializer`'s "isSerializer: true" flag only
sets the SERIALIZER content type default; it does NOT set the DESERIALIZER default — that's a separate flag
only `AddDeserializer(..., isDefault: true)` (or the `DefaultContentType` property) sets.

**This pairing is the correct pattern for ANY future custom `ISerializerFactory` registration in this domain**,
not just payload transform — e.g. a future custom envelope format, a Protobuf/MessagePack serializer swap, etc.

## Debugging technique that found this: register `SharedKernel.Testing.Logging.InMemoryLoggerFactory`

To see MassTransit's own internal logs (which reveal the REAL exception even when `harness.Consumed`/
`harness.Published.Any<Fault<T>>()` both report nothing), register the 16.Testing in-memory logger factory
into the test's `ServiceCollection` alongside `.AddLogging()`, run the scenario, then dump every captured
`LogRecord` (including the `[MassTransit.ReceiveTransport] Error` level ones — MassTransit logs receive-pipe
deserialization failures there even when it can't surface a typed `Fault<T>`). This is far more informative
than guessing from `harness.Consumed`/`harness.Published` alone for "message published but never consumed,
no fault either" scenarios. Delete the logging scaffolding once the real exception is found — it's a debug
aid, not something to ship in a production-quality test.

## `Headers` interface — real shape and a real, direct-constructible implementation exists

`MassTransit.Headers` extends `IEnumerable<HeaderValue>` (both generic and non-generic `GetEnumerator()`) PLUS:
```csharp
IEnumerable<KeyValuePair<string, object>> GetAll();
bool TryGetHeader(string key, out object? value);
T Get<T>(string key, T defaultValue) where T : class;
T? Get<T>(string key, T? defaultValue = null) where T : struct;
```
Hand-rolling a test double for this interface is real work (5+ members). Don't — MassTransit ships public,
directly-constructible implementations:
- `MassTransit.Serialization.DictionarySendHeaders` — **public parameterless `ctor()`** — use this for a
  test double needing an empty/mutable `Headers` instance.
- `MassTransit.Serialization.EmptyHeaders` — also public, but has **no public constructor found** (internal
  construction only) — do NOT try to `new` it directly, it fails with CS1729.

Both require `using MtEmptyHeaders = MassTransit.Serialization.DictionarySendHeaders;`-style aliasing (or
`global::MassTransit.Serialization.X`) when referenced from a file under the `SharedKernel.Messaging.MassTransit*`
namespace tree, per the established `[[project_masstransit_ordered_delivery]]`/header-propagation namespace-
shadowing pattern — `MassTransit.Serialization` collides with the enclosing `SharedKernel.Messaging.MassTransit`
segment.

## Testing pattern for a custom serializer/deserializer pair

1. **Unit-level round-trip** (fastest signal): construct the real inner factory
   (`new MassTransit.Configuration.SystemTextJsonMessageSerializerFactory(configure: null)`), wrap it with your
   decorator, and drive `serializer.GetMessageBody<T>(sendContext)` → `deserializer.Deserialize(body, headers, uri)`
   directly — no bus, no harness. Needs a `SendContext<T>` instance; NSubstitute can mock it
   (`Substitute.For<SendContext<TMessage>>()`) but **the message type `TMessage` must be `public`**, not
   `internal` — Castle DynamicProxy (NSubstitute's proxy engine) refuses to generate a proxy involving a
   non-public type from another assembly (`System.ArgumentException: ... type X is not accessible`). This is
   unrelated to the established "use internal for real bus/harness message types" rule — that rule is about
   MassTransit's own type-name-based endpoint/URN resolution, not NSubstitute proxying, so this SendContext-mock
   probe technique needs its own throwaway `public` message record, separate from harness test messages.
2. **Full end-to-end** via `AddMassTransitTestHarness` + `UsingInMemory`, calling your `ConfigurePayloadTransform`-
   style internal static helper directly inside the `(ctx, busCfg) => {...}` callback (mirrors the established
   `MessagingBusBuilder.ConfigureRabbitMq`/`ConfigureAzureServiceBus`/`ConfigureDeadLetterPolicy` internal-for-
   testability pattern) — this is the ONLY way to prove the `ClearSerialization()`/`AddDeserializer` wiring
   actually works, since the unit-level round-trip in (1) never exercises the bus's serializer REGISTRATION at
   all, only the decorator objects' own logic in isolation.
3. **Mismatch tests**: exercise `PayloadTransformMessageDeserializer` directly (not through a full two-bus
   setup — TestHarness's `UsingInMemory` is one bus, so simulating "publisher transformed, consumer didn't" via
   two interoperating harness instances is unnecessarily complex). Construct genuinely-transformed bytes using
   the real `IPayloadCompressor`/`ISymmetricEncryptionService` directly (bypassing the serializer), then feed
   them into a deserializer configured with MISMATCHED `PayloadTransformOptions` and assert the wrapper exception
   type. Both mismatch directions (publisher-on/consumer-off and the reverse) are provable this way with zero
   MassTransit bus involvement at all.

**Why:** discovered while implementing SK.07.PayloadTransform (P-346/WO-054, 2026-08-06). See
[[project_masstransit_testing_patterns]] for the general TestHarness gotchas this extends, and
[[project_masstransit_header_propagation]]/[[project_masstransit_ordered_delivery]] for the established
namespace-aliasing and `dotnet test`-based (not `dotnet run`, which hits the license gate) reflection-probe
techniques this phase reused.
