# Memory Index

- [Project: SharedKernel Caching Domain](project_caching_domain.md) — three-package split, FusionCache/Redis/RedLock versions, established patterns
- [DI Registration Conventions](project_di_conventions.md) — ICachingBuilder extension patterns, IConnectionMultiplexer registration rules
- [Test Patterns](project_test_patterns.md) — Testcontainers Redis setup, TestCachingBuilder helper, namespace fixes across phases
- [Layering Fix Patterns](project_layering_fix.md) — CachingCoreOptions pattern for cross-provider shared options, sibling package rules
- [Redis Package Split (WO-023, COMPLETE)](project_redis_package_split.md) — Phases 32-36 done; Redis.Core shape, consumption pattern, standalone-extraction template, hidden-test-coverage grep step, FakeCacheService DI pattern
- [NuGet Packaging Parity (WO-050 Phase 38, COMPLETE)](project_nuget_packaging_parity.md) — NuGet global-cache same-version gotcha, consumer-verify PackageReference rebuild pattern (5 surfaces), README/metadata template
