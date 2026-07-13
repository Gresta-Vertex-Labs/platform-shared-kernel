---
name: loggermessage-syslib1019-field-requirement
description: [LoggerMessage] source generator requires an ILogger FIELD, not a property, on the containing type — SYSLIB1019/CS8795 workaround discovered during WO-041 P-254 logging retrofit
metadata:
  type: project
---

The `[LoggerMessage]`-attributed partial method source generator (`Microsoft.Extensions.Logging.Abstractions`)
only auto-discovers an `ILogger`-typed **field** on the containing type. It does NOT see an
`ILogger`-typed **property** (even a trivial `{ get; }` auto-property) — this produces
`SYSLIB1019` ("no ILogger field found") followed by `CS8795` ("partial method with accessibility
modifiers must have an implementation part") if you declare an instance `private partial void
LogXxx(...)` method on a type whose only logger member is a property.

**Why:** discovered in `07.Messaging/SharedKernel.Messaging.MassTransit` during the WO-041 P-254
`[LoggerMessage]` retrofit. `ConsumerBase<TMessage>`, `BatchConsumerBase<TMessage>`, and
`RoutingSlipActivityBase<TArguments,TLog>` all expose `protected ILogger Logger { get; }` (a
property, chosen for subclass-overridability) — every `[LoggerMessage]` method on these three
types had to become `private static partial void LogXxx(ILogger logger, ...)` with an explicit
`ILogger logger` parameter, with call sites passing `Logger` explicitly (e.g.
`LogConsumeError(Logger, typeof(TMessage).Name, ex);`). By contrast, `FaultConsumerAdapter` and
`VersionTranslatingConsumer` use a private constructor-injected `ILogger<T>` **field**, so their
`[LoggerMessage]` methods stayed ordinary instance `private partial void` methods with zero
workaround needed.

**How to apply:** before retrofitting or newly authoring `[LoggerMessage]` methods on any base
class across the SharedKernel monorepo:
1. Check whether the containing type's logger member is a field or a property.
2. If it's a property, either (a) convert the member to a private field if subclass
   overridability of the logger itself isn't a genuine requirement (avoids the workaround
   entirely — this is the preferred fix for new code), or (b) declare the `[LoggerMessage]`
   method as `static partial` with an explicit `ILogger logger` parameter and pass the property
   value at every call site.
3. This applies to ANY domain in the repo, not just `07.Messaging` — the same generator
   limitation will bite `05.Application.Behaviors`, `11.Communication.*`, `14.Presentation.*`,
   `15.Integration`, etc. if any of them expose a protected `ILogger` **property** on a base
   class future logging retrofits touch. Check for this pattern before assuming an instance
   `partial` method declaration will "just work."

Full detail recorded in `07.Messaging/CLAUDE.md`'s "Logging authoring standard and EventId
allocation (P-254)" section, immediately after the EventId allocation table.
