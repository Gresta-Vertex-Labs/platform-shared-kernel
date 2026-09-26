# Memory Index

- [Project: SharedKernel Caching Domain](project_caching_domain.md) — pre-WO-086 history: three-package split, FusionCache/Redis/RedLock versions, established patterns
- [Test Patterns](project_test_patterns.md) — Testcontainers Redis setup, TestCachingBuilder helper, namespace fixes across phases
- [Redis Package Split (WO-023, COMPLETE)](project_redis_package_split.md) — Phases 32-36 done; Redis.Core shape, consumption pattern, standalone-extraction template, hidden-test-coverage grep step, FakeCacheService DI pattern
- [NuGet Packaging Parity (WO-050 Phase 38, COMPLETE)](project_nuget_packaging_parity.md) — NuGet global-cache same-version gotcha, consumer-verify PackageReference rebuild pattern (5 surfaces), README/metadata template
- [WO-050 Gold-Standard Follow-Up (Phases 38-41)](project_wo050_gold_standard.md) — Testcontainers.Redis 4.4.0→4.13.0 version-pin regression fix, confirmed FusionCache tag-invalidation backplane mechanism, redis-cli MONITOR diagnostic technique
- [WO-065 Phase 42 (Cache Encryption, COMPLETE)](project_wo065_ph42_cache_encryption.md) — Redis stores L2 entries as a Hash not a string (HashGetAsync not StringGetAsync), DI-marker ordering-guard pattern, descriptor-capture decorator-wrapping pattern, recurring stale-pin-vs-SharedKernel.Testing defect class
- [WO-081 Phase 46 (EncryptedCacheService, COMPLETE)](project_wo081_ph46_encrypted_cache_service.md) — CacheEncryptionSerializer retired for an ICacheService-level decorator (key-bound AAD), FusionCache envelope nests stored value under "Value" (raw-Redis JSON assertions must account for this), factory-recapture test pattern proves no-AsyncLocal without real eager-refresh timing, concurrent-sibling-domain red builds self-resolve
