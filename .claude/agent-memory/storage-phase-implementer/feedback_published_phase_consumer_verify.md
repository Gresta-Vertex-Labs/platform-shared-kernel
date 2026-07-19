---
name: feedback_published_phase_consumer_verify
description: Repo-wide pattern for the Published-phase consumer-verify harness (standalone console project, not xUnit) and how to prove IHost.StartAsync() config-validation failures
type: feedback
---

The Published phase's "consumer-verify harness" tasks (P-03..P-06 in 08.Storage's case) are NOT xUnit
tests inside a `.Tests` project — they are a **separate, standalone console project** named
`consumer-verify` at the domain root (sibling to the package folders, not nested inside any one package),
mirroring an already-established repo-wide precedent visible in `13.ServiceDefaults/consumer-verify/`,
`14.Presentation/consumer-verify/`, `15.Integration/consumer-verify/`, and `04.Contracts/consumer-verify/`.

**Why:** the point of this harness is to exercise the packages exactly as a downstream microservice would
— real DI composition, real `ProjectReference` (standing in for a packed NuGet reference; the compiled
surface is identical), no test-framework scaffolding, no mocking. A `.Tests` project's own DI-resolution
tests (already written in the Tests phase) prove the same thing in isolation with hand-rolled
`ServiceCollection` setups; this harness proves it end-to-end via `dotnet run`.

**Shape**, confirmed against `13.ServiceDefaults/consumer-verify/consumer-verify.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup><ProjectReference Include="...package csproj paths..." /></ItemGroup>
</Project>
```
Register it in `Platform.SharedKernel.slnx` under the domain's `<Folder>`. `Program.cs` is top-level
statements: one `static async Task SurfaceN_Description()` local function per acceptance-criterion task,
called in sequence from the top, each ending with `Console.WriteLine("Surface N PASS: ...")`; a `Verify(bool,
string)` local helper throws `InvalidOperationException` on failure (no xUnit `Assert`). Finish with `ALL
SURFACES VERIFIED — consumer-verify PASSED`.

**Proving "fails at `IHost.StartAsync()`" (the P-06-shaped task) requires a real generic host, not
`BuildServiceProvider()`:** `SharedKernel.Configuration.AddValidatedOptions<T>()` calls `.ValidateOnStart()`,
which registers a hosted service that runs `ValidateDataAnnotations()` during `IHost.StartAsync()` — a plain
`services.BuildServiceProvider()` never triggers it. Use `Host.CreateApplicationBuilder()` (needs an explicit
`PackageReference Include="Microsoft.Extensions.Hosting"` — pin to the same version already used elsewhere in
the repo, `10.0.9` as of 2026-07; older `9.0.5` also appears in a few earlier-session `.Tests` projects,
`10.0.9` is the current floor) to get default logging (satisfies any `ILogger<T>` constructor dependency in
the package's DI-registered types) plus real config, register the package's `AddSharedKernelXStorage()`
(or equivalent) with a deliberately incomplete config section, `builder.Build()`, then `try { await
host.StartAsync(); } catch (OptionsValidationException ex) { caught = ex; }` — assert `caught is not
null` and `caught.Failures.Any(f => f.Contains("PropertyName"))` to prove the message is actionable
(names the specific missing field), not generic. `Host.CreateApplicationBuilder()`'s
`builder.Configuration.AddInMemoryCollection(...)` needs no extra package reference — `Microsoft.Extensions.
Configuration.Memory` comes in transitively via `Microsoft.Extensions.Hosting`.

**Proving side-by-side keyed-DI registration (a C-29/DO-06-shaped task):** if the domain's README already
documents a fully-worked `AddKeyedSingleton` example for composing two sibling providers, copy that exact
code into a harness surface using a plain `ServiceCollection` (not a host) — this both proves the harness
and proves the README's own example actually compiles/runs against real shipped code for the first time
(the README was written by a prior Docs-phase session and had never been executed as code).

Full worked example: `08.Storage/consumer-verify/Program.cs` (2026-07-18 session).
