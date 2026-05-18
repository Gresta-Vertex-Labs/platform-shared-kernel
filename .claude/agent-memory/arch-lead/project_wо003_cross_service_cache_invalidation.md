---
name: wo003-cross-service-cache-invalidation
description: Architectural decision for ICacheInvalidationBus — cross-service L1 cache invalidation pattern in 02.Caching, WO-003
metadata:
  type: project
---

Cross-service cache invalidation broadcast is owned by `02.Caching` as P-012 (WO-003).

**Why:** FusionCache's built-in Redis backplane handles same-service multi-instance L1 sync. It does NOT handle cross-service invalidation (Service A telling Service B to drop its L1). This gap required a new first-class abstraction.

**Decision — `ICacheInvalidationBus`, not extending `IRedisChannelService`:**
`IRedisChannelService` is a general-purpose Pub/Sub transport; `ICacheInvalidationBus` is a cache-domain-specific higher-level abstraction. Extending the transport would violate Interface Segregation and force cache semantics onto the transport interface. The implementation (`RedisCacheInvalidationBus`) uses `IRedisChannelService` as transport internally — composition over inheritance.

**Channel naming convention:** `sharedkernel:cache:invalidation:{service-name}` (targeted) and `sharedkernel:cache:invalidation:broadcast` (platform-wide flush).

**Wire payload:** `CacheInvalidationMessage` sealed record — `SourceService`, `InvalidationType` (Key/Tag/All), `Keys[]?`, `Tags[]?`, `CorrelationId`, `TimestampUtc`. STJ source-generated serialization for AOT safety.

**Receiver:** `CacheInvalidationReceiver` `BackgroundService` auto-subscribes on startup (opt-in via DI). Calls `ICacheService.RemoveAsync`/`RemoveByTagAsync`. Creates OTel span with sender's `CorrelationId`. Never throws — all errors logged.

**No delivery guarantee** — explicitly documented on the interface. If a service is offline, the invalidation is lost; TTL is the fallback. This is the correct boundary between 02.Caching and 07.Messaging.

**P-013** (16.Testing) adds `FakeCacheInvalidationBus` with `OnInvalidation` handler pattern to let tests wire `FakeCacheInvalidationBus → FakeCacheService` without Redis.

**Why:** FusionCache backplane = same-service multi-replica L1 sync. ICacheInvalidationBus = cross-service L1 sync. Different concerns at different levels.

**How to apply:** When a future request involves cache propagation between different services, check whether it is same-service (use FusionCache backplane) or cross-service (use `ICacheInvalidationBus`). Never conflate the two.
