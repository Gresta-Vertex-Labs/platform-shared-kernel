---
name: feedback_redis_test_ttl_must_not_borrow_shared_default
description: A real CI failure in T-03 (order-independence) traced to borrowing T-02's short InFlightTtl test default; how it was diagnosed and the general rule going forward.
type: feedback
---

> WO-086 (2026-09): `HasProcessedAsync`/`MarkProcessedAsync`/`StoreResponseAsync`, `IIdempotencyResponseStore`, `IdempotentCommandBehavior` and the `InFlightTtl`/`RetentionWindow` options were deleted — the in-flight TTL is now `IIdempotencyStore.TryBeginAsync`'s `ttl` argument and retention `CompleteAsync`'s `retention` argument. The rule still holds in that form: each Redis test passes its own generous `ttl` unless it tests expiry.

**Rule: every `.Redis` Testcontainers test that does more than 1-2 sequential store round trips
before its final assertion must pass its own explicit `RedisIdempotencyOptions` — never rely on a
shared test-helper's short `InFlightTtl` fallback meant for a *different* test.**

**Why:** `RedisIdempotencyConcurrencyTests`'s `CreateKeyStore` helper defaults to
`InFlightTtl = TimeSpan.FromMilliseconds(300)` when no options are passed — a value sized
specifically for T-02 (`HasProcessedAsync_AfterInFlightTtlElapsesWithoutConfirmation_BecomesRetryable`),
which *wants* a short TTL so the self-heal-on-expiry test doesn't take long. T-03
(`ConfirmThenStoreResponse_InEitherOrder_PreservesTheStoredResponse`) called the same helper with no
options, silently inheriting that same 300ms value, even though T-03 has nothing to do with expiry —
it verifies that `PEXPIRE` (confirm) and the Lua response-write script never clobber each other. T-03
does `reserve -> StoreResponseAsync -> MarkProcessedAsync -> read`, three-plus real network round
trips to the Testcontainers Redis instance. On a slow/contended 2-core GitHub Actions runner those
round trips cumulatively exceeded 300ms, Redis evicted the whole key (sentinel + response together)
between the write and the confirm, and the test failed with the response reading back `null` — a real
CI failure (see [[project_18idempotency_first_implementation]] for the session that shipped this
domain; this defect surfaced later, once the integration/Testcontainers CI lane was actually exercised
for the first time).

**How to apply:** when writing or reviewing a `.Redis` concurrency/behavioral test:
1. If the test is specifically about expiry/self-heal timing (T-02's shape), a short `InFlightTtl` is
   correct and intentional — keep it, and keep it *local* to that test via the `options` parameter.
2. Any other test that chains `HasProcessedAsync` -> (`StoreResponseAsync`/`MarkProcessedAsync` in
   any order) -> a read, with no reason to test expiry, must pass `new RedisIdempotencyOptions()`
   explicitly (production defaults: 30s `InFlightTtl`, 24h `RetentionWindow`) so it is never
   accidentally coupled to whatever short value a sibling test's helper call happens to default to.
3. To verify a timing hypothesis like this one, don't trust "it passed on my machine" — inject a
   real `Task.Delay` between the two calls under suspicion, long enough to exceed the suspect TTL,
   and confirm the failure reproduces *deterministically* before touching any code. Then prove the
   fix by re-running the identical injected delay against the corrected (generous-TTL) setup and
   confirming it now survives. Both directions of proof matter — reproducing the failure and then
   proving the fix survives the same induced delay — not just "the flaky test passed a few times."

**Root-cause note (not a bug in the store):** the production call order (from
`IdempotentCommandBehavior`) is always confirm-then-store (`MarkProcessedAsync` before
`StoreResponseAsync` — see `IIdempotencyResponseStore`'s own XML doc: "HasProcessedAsync/
MarkProcessedAsync are always called first, regardless of replay support"). That order is
structurally immune to this race at *any* `InFlightTtl` size, because confirm extends the key's TTL
to the long `RetentionWindow` before any response write happens. T-03's only-tested order
(store-then-confirm) is the reverse, defensive direction — a real property worth testing, but one
that actually needs TTL headroom to prove cleanly. The fix converted T-03 into a `[Theory]` covering
both orders explicitly rather than just widening the shared default, so the test now documents *why*
each order is safe instead of merely not-racing.

**`.EfCore` does not share this defect class** — see [[reference_efcore_reservation_protocol]] and
[[reference_redis_reservation_protocol]]'s note on the deliberate `IClock` asymmetry: `.EfCore`'s
expiry is a `WHERE expires_at_utc > now` comparison against an injected `IClock` snapshot per call,
never a database-server-enforced real-time TTL eviction. No amount of real wall-clock slowness
between sequential test round trips can make an EfCore row spontaneously disappear the way a Redis
key can. `.EfCore` does have its own asymmetry worth remembering for later, though: unlike
`StoreResponseAsync`, `MarkProcessedAsync`'s `ExecuteUpdateAsync` carries no
`WHERE expires_at_utc > now` guard — a sufficiently late confirm could extend whatever row currently
occupies that `(TenantId, Key)`, including one a second caller already reclaimed. This mirrors
Redis's own symmetric, equally-unguarded `KeyExpireAsync`/no-fencing-token behavior (Domain Invariant
2's documented self-heal trade-off), so it isn't a new asymmetry — just something to flag if a
fencing/version token is ever proposed for either store.
