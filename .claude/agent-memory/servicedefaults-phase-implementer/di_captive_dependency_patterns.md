---
name: di_captive_dependency_patterns
description: How to safely resolve optional/Scoped dependencies from Singleton-lifetime code in this domain (IValidateOptions, PostConfigure hooks, Kestrel delegates) without breaking bare-ServiceCollection test hosts or hitting captive-dependency errors
metadata:
  type: project
---

**Problem class:** this domain repeatedly needs to read a DI-registered value from code that runs
outside any request scope (Kestrel TLS handshake, `IValidateOptions<T>.Validate` at
`ValidateOnStart()` time, an `Add*Check` extension method's own registration path before
`IHostApplicationBuilder.Build()` exists). Naively constructor-injecting the real dependency breaks
in one of two ways depending on the dependency's lifetime.

**Pattern A — dependency is Scoped, consumer must be Singleton (or resolved from root):**
Never constructor-inject the Scoped type directly. Capture `IServiceProvider` (or
`IServiceScopeFactory`) instead, and create a fresh `IServiceScope` per call. Established first by
`MtlsClientCertificateExtensions.AddMtlsClientCertificate` (C-48, WO-058) for
`IMtlsCertificateValidator`, reused for `TenantResolutionOptionsValidator` (C-54, WO-061): it
captures `IServiceProvider` rather than constructor-injecting `IEnumerable<ITenantResolutionStrategy>`
(the platform's three built-in strategies are registered Scoped, but `IValidateOptions<T>` must be
Singleton to be resolved by `ValidateOnStart()`'s root-provider validation pass — a Singleton
consuming a Scoped constructor dependency is a captive-dependency error, and under
`ServiceProviderOptions.ValidateScopes = true` — the Development default — it throws outright).
`Validate()` does `using var scope = serviceProvider.CreateScope(); scope.ServiceProvider.GetServices<T>()`
per call. Cheap since these validation/handshake paths are low-frequency (once at startup, once per
TLS handshake), not a hot request path.

**Pattern B — dependency is genuinely optional (may not be registered at all), consumer is a
`PostConfigure<TDep>` hook on an `OptionsBuilder<T>`:**
`OptionsBuilder<T>.PostConfigure<TDep>(Action<T,TDep>)` always resolves `TDep` via
`GetRequiredService<TDep>()` internally — there is no "optional PostConfigure" overload. If `TDep`
is something a bare test host might not register (e.g. `ILoggerFactory` in a plain
`new ServiceCollection()` with no `AddLogging()`), this throws
`InvalidOperationException: No service for type '...' has been registered` the moment the options
value is resolved — breaking any test that doesn't opt into logging.

Fix used for `HealthCheckRegistrationLogging` (C-53, WO-061): `PostConfigure<IServiceProvider>`
instead — `IServiceProvider` is *always* resolvable in any container (the container registers
itself) — then inside the callback, `serviceProvider.GetService<ILoggerFactory>()?.CreateLogger(...)`
with a manual null-check before logging. This was a genuine regression caught by the existing
`HealthCheckNamesTests`/`HealthCheckTagTests` suite (18 pre-existing tests failed the first time this
was written as `PostConfigure<ILoggerFactory>` directly) — always re-run the full existing suite
after adding a `PostConfigure` hook to a shared options type multiple `Add*` methods all touch.

**Pattern C — `HttpContext.RequestServices` can itself be null**, not just the service it resolves.
A bare `new DefaultHttpContext()` in a unit test has no `IServiceProvidersFeature` attached, so
`context.RequestServices` is `null` by default (only real ASP.NET Core hosting, or a test that
explicitly sets `RequestServices = someProvider`, populates it). When resolving a genuinely-optional
service from `HttpContext` (e.g. `ITenantStatusValidator` in `TenantResolutionMiddleware`, C-58),
always null-conditional the accessor itself: `context.RequestServices?.GetService<T>()` — not just
`context.RequestServices.GetService<T>()`. Missing this breaks every existing test that constructs a
bare `HttpContext` without setting `RequestServices`.

See also: [[otel_wiring_pattern]] for the sibling "always re-run the existing suite before declaring
a shared-registration-path change done" discipline.
