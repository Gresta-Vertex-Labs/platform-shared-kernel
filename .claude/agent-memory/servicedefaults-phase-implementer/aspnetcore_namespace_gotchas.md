---
name: aspnetcore_namespace_gotchas
description: Non-obvious namespace locations for ASP.NET Core extension methods this domain uses, confirmed by decompiling installed assemblies rather than guessed
metadata:
  type: project
---

**`AddRateLimiter`/`RateLimiterOptions`/`RequireRateLimiting`/`UseRateLimiter` all live in namespace
`Microsoft.AspNetCore.Builder`** (classes `RateLimiterServiceCollectionExtensions`,
`RateLimiterApplicationBuilderExtensions`, `RateLimiterEndpointConventionBuilderExtensions`) —
**not** `Microsoft.Extensions.DependencyInjection`, despite `AddRateLimiter` being an
`IServiceCollection` extension method (the namespace convention most other `Add*` DI registration
extensions in the platform follow). Confirmed via `ilspycmd -l c` type-listing against
`Microsoft.AspNetCore.RateLimiting.dll` in the installed net10.0 shared framework
(`C:\Program Files\dotnet\shared\Microsoft.AspNetCore.App\10.0.10\`), not assumed from memory —
a first guess of `using Microsoft.Extensions.DependencyInjection;` alone does NOT resolve
`AddRateLimiter` and produces CS1061. `RateLimiterOptions`/`RateLimitPartition`/
`FixedWindowRateLimiterOptions` (the config-shape types) are in `Microsoft.AspNetCore.RateLimiting`
as expected.

**`ConfigurationManager` (what `IHostApplicationBuilder.Configuration` actually is for both
`WebApplicationBuilder` and `Host.CreateApplicationBuilder()`) rebuilds its `IConfigurationRoot`
eagerly and synchronously on every `.Add(...)` call** — unlike the classic `ConfigurationBuilder`
pattern where every source's `Load()` is deferred until an explicit `.Build()`. This is why
`AddSharedKernelKeyVaultConfiguration` (C-56, WO-061) needed no extra explicit connectivity check
beyond calling `builder.Configuration.AddAzureKeyVault(...)` itself — `AzureKeyVaultConfigurationProvider
.Load()` (which blocks synchronously on the async secret-enumeration call) runs, and can throw,
directly from that one call, before the extension method returns. Verified via a real test against
an unreachable loopback endpoint (`https://127.0.0.1:1/` — port with no listener, fails via
connection-refused near-instantly rather than a slow DNS timeout) paired with a fake,
instantly-resolving `TokenCredential` subclass to isolate the test from `DefaultAzureCredential`'s
multi-source probing latency (Environment → Managed Identity → Azure CLI → ... each has its own
timeout).

**`ilspycmd -l c <dll>`** (list all classes) is the fast first move when you know the assembly but
not the exact namespace of an extension-method class — faster than guessing-and-CS1061-iterating.
See [[decompile_verification_technique]] for the fuller decompile workflow this domain has used
repeatedly (Polly Meter-vs-ActivitySource, Kestrel private delegate field, now this).
