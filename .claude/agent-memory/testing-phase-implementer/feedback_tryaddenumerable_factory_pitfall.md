---
name: feedback_tryaddenumerable_factory_pitfall
description: TryAddEnumerable + a single-type-parameter factory ServiceDescriptor is broken for registering N instances of the SAME concrete fake type — always run the DI test before trusting the design doc's "mirrors TryAddEnumerable" claim.
type: feedback
---

`services.TryAddEnumerable(ServiceDescriptor.Singleton<TService>(factory))` (the ONE-type-parameter
factory overload) throws `ArgumentException` ("Implementation type cannot be 'X' because it is
indistinguishable from other services registered for 'X'") on the very FIRST call, not just on a
duplicate. .NET's `TryAddEnumerable` infers a descriptor's "implementation type" from the factory
delegate's own `Func<IServiceProvider, TService>` generic signature — not from what the factory
actually constructs at runtime — so a `Func<IServiceProvider, ICacheWarmupStrategy>` factory looks
identical to the service type itself.

Switching to the TWO-type-parameter overload (`ServiceDescriptor.Singleton<TService, TImplementation>(factory)`)
fixes the crash but does not fix the real goal if the goal is "register N independently-configured
instances of the SAME concrete type" (e.g. a fake constructed with a different `name`/`order` each
call) — `TryAddEnumerable` de-duplicates by `(ServiceType, ImplementationType)` only, with NO
awareness of constructor arguments. Every call after the first is silently DROPPED, never appearing
in the resolved `IEnumerable<TService>`. This is invisible without a test that resolves via
`IServiceCollection` → `IEnumerable<TService>` and checks the count — a design doc that says "mirrors
production's `TryAddEnumerable` multi-strategy shape" can be wrong even when it compiles and even
when a *single* registration test passes.

**Why:** `TryAddEnumerable` is only correct for the "N different concrete types implement the same
interface" case (e.g. real `AddCacheWarmup<TStrategy>()`, genuinely parameterized by a distinct
generic `TStrategy` per call). It is the WRONG tool whenever a single concrete type is parameterized
by runtime values instead of a compile-time type argument — in that case, use plain `AddSingleton`/
`AddScoped`/`AddTransient` (no de-duplication), since every call is intentionally a new, independent
registration.

**How to apply:** Before shipping ANY `Add*` fake-registration extension in `16.Testing` (or
reviewing one) that is meant to support "call this multiple times with different arguments, all
show up in `IEnumerable<TInterface>`", write the multi-call resolution test FIRST (or at least before
declaring the phase done) — a 3-line `services.AddFakeX("a"); services.AddFakeX("b"); provider.GetServices<IX>().Count() == 2` check catches both failure modes (crash-on-first-call AND
silent-drop-after-first-call) immediately. Never trust "the CLAUDE.md design doc says it mirrors
`TryAddEnumerable`" as proof it works — this exact claim was in the shipped Core-phase code, shipped
`CLAUDE.md`, and shipped `state-map.md` for this domain and was wrong in a way that only a resolution
test surfaced (found during `SK.16.Tests`/T-64 for `AddFakeCacheWarmupStrategy()`, WO-050/P-306,
2026-07-29).
