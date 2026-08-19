# Memory Index

- [OTel wiring pattern](otel_wiring_pattern.md) — string-name-only AddSource/AddMeter wiring, never reference owning domain's internal diagnostics type
- [Cross-domain blocking pattern](crossdomain_blocking_pattern.md) — how to verify an upstream-domain block is resolved before implementing, and what to clean up in both state-maps after
- [Domain facts](domain_facts.md) — package/folder layout, layering exception, health check tag taxonomy, test counts (refresh if stale)
- [BaggageLogRecordProcessor](baggage_log_record_processor.md) — Activity.Baggage→LogRecord.Attributes pattern, OTel logging test technique, test-only cross-package ProjectReference precedent
- [Decompile verification technique](decompile_verification_technique.md) — ilspycmd workflow to verify third-party telemetry (Polly has Meter not ActivitySource; TryAddSingleton = free idempotency)
- [gRPC/Polly test capture pattern](grpc_polly_test_capture_pattern.md) — WebApplicationFactory+Grpc.Net.Client real-call span proof, Polly ConfigureTelemetry+InMemoryExporter metric proof (T-43)
- [Kestrel private-delegate reflection technique](kestrel_private_delegate_reflection_technique.md) — prove ConfigureHttpsDefaults wiring via reflection over KestrelServerOptions.HttpsDefaults, no real TLS handshake (T-44)
- [DI captive-dependency patterns](di_captive_dependency_patterns.md) — IServiceScopeFactory-per-call for Scoped-from-Singleton, `PostConfigure<IServiceProvider>` for optional deps, HttpContext.RequestServices can be null (C-49–C-58)
- [ASP.NET Core namespace gotchas](aspnetcore_namespace_gotchas.md) — AddRateLimiter lives in Microsoft.AspNetCore.Builder not DependencyInjection; ConfigurationManager loads eagerly/synchronously on .Add() (C-55/C-56)
