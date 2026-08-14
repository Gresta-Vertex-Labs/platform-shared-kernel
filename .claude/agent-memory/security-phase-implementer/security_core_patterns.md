---
name: security-core-patterns
description: Key implementation decisions from SK.12.Core — claims mapping, DI registration, AOT constraints, test patterns
metadata:
  type: project
---

## OidcUserContext — IsAuthenticated invariant (WO-057/P-367, SHIPPED 2026-08-13)

Corrected invariant, now live: the old rule ("`IsAuthenticated` forced false whenever `sub` is absent/
unparseable") conflated a rejected/unauthenticated caller with a legitimate client-credentials (M2M) token
that simply has no human subject. Current rule: `UserId` is never `Guid.Empty` only when
`IdentityKind == User`; `IdentityKind.ServicePrincipal`/`.System` legitimately carry `IsAuthenticated = true`
with `UserId == Guid.Empty`. `IdentityKind` resolution inside `OidcUserContext`'s constructor:
`User` when `principal.Identity?.IsAuthenticated == true` AND a parseable non-empty-Guid `sub` claim is
present; `ServicePrincipal` when `IsAuthenticated == true` but no such claim (logs `SecurityLogEvents
.ServicePrincipalRecognized`, EventId 12100, Debug); `Anonymous` only when the underlying
`ClaimsPrincipal.Identity` itself is not authenticated (no log — routine/expected). Detection is IdP-agnostic
— never hardcode one vendor's claim names (e.g. Entra's `idtyp`/`azp`).

**Constructor shape (WO-057)**: `OidcUserContext(ClaimsPrincipal principal, ClaimMappingOptions claimMapping,
ILogger<OidcUserContext>? logger = null)` — the second parameter is REQUIRED (not `IOptions<SecurityOptions>`
as originally drafted; the DI factory extracts `.Value.ClaimMapping` before calling the constructor), the
logger is optional/nullable so direct/non-DI construction (tests) works without one. `OidcTenantProvider`
gained the identical optional-`ILogger<OidcTenantProvider>?` pattern, logging `TenantClaimResolutionFailed`
(EventId 12101, Warning) — but ONLY when the principal is authenticated; an anonymous request resolving to
`Guid.Empty` is expected, not a signal.

**Defensive role-claim reader**: `OidcUserContext` reads role claims by iterating `principal.Claims` directly
(never the first-value-wins `Claims` dictionary, which would lose multi-value roles) and handles TWO shapes:
one `Claim` per role, or a single claim whose value is a JSON array (`["admin","editor"]`, sniffed via a
cheap `[`/`]` bracket check before attempting `JsonDocument.Parse` — falls back to treating the raw value as
a single non-array entry on any parse failure, never throws).

**Permissions**: space-delimited single claim (default type `"scope"`, configurable via
`ClaimMappingOptions.PermissionClaimType`), split via `string.Split(' ', RemoveEmptyEntries | TrimEntries)`.

## AnonymousUserContext fallback pattern

Scoped DI factory for `IUserContext`:
```csharp
services.AddScoped<IUserContext>(sp => {
    var accessor = sp.GetRequiredService<IHttpContextAccessor>();
    var user = accessor.HttpContext?.User;
    return user is not null ? new OidcUserContext(user) : AnonymousUserContext.Instance;
});
```
`AnonymousUserContext.Instance` is a static readonly singleton — avoids allocation when no HTTP context is present (background workers, console hosts, unit-test DI containers).

## OidcTenantProvider — never throws

Returns `Guid.Empty` for absent or malformed `tenant_id` claim. Never throws. Callers must handle `Guid.Empty` (unauthenticated or system-level requests).

## SecurityOptions binding

- Section key: `"Security"` (constant `SecurityOptions.SectionKey`)
- Nested: `JwtOptions` with `Authority` (required), `Audience` (required), `ValidateLifetime` (default `true`), `ClockSkewSeconds` (default `30`)
- Uses `AddValidatedOptions<SecurityOptions>` from `SharedKernel.Configuration` — startup fails at `IHost.StartAsync()` when required fields are missing
- JWT Bearer post-configured via `IOptions<SecurityOptions>` — no `BuildServiceProvider()` anti-pattern

## JWT validation defaults

`ValidateIssuer = true`, `ValidateAudience = true`, `ValidateLifetime = true` by default.
Any relaxation must be explicit and documented at the call site.

## AOT constraints

- `Microsoft.Identity.Web` isolated entirely to `AddAzureB2CAuthentication` — not AOT-safe; swap to standard Entra ID path avoids AOT blast radius
- `Microsoft.AspNetCore.Authentication.JwtBearer` has partial AOT support (internal reflection in token parsing) — encapsulated behind `IUserContext` so blast radius is limited to DI registration only
- `OidcUserContext`/`OidcTenantProvider` claims iteration is AOT-safe (no reflection on user types)

## Test construction patterns

**Authenticated principal:**
```csharp
new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"))
// authenticationType string → IsAuthenticated = true
```

**Unauthenticated principal:**
```csharp
new ClaimsPrincipal(new ClaimsIdentity(claims))
// no authenticationType → IsAuthenticated = false
```

**DI registration tests:** Use `ServiceCollection` + `BuildServiceProvider()` directly. No `WebApplicationFactory` or test host required for unit-level DI verification.

## SecurityClaimTypes constants

- `UserId` = `"sub"`
- `TenantId` = `"tenant_id"`
- `Email` = `ClaimTypes.Email`
- `Role` = `ClaimTypes.Role`

## Phase completion

Original build-out (2026-06-02): all six phases ● for `.Abstractions`/`.Oidc`, 46 tests passing (13 + 33).
Both packages produce `.nupkg` + `.snupkg` via `dotnet pack --configuration Release`, output to `artifacts/nupkg/`.

**WO-057 (2026-08-13):** arch-lead dispatched a 7-phase gold-standard review (P-366–P-372) reopening
all six phase keys to `◐`/re-close them one at a time as each work order phase lands.
`SK.12.Design` re-closed 22/22 (●); `SK.12.Scaffold` re-closed 15/15 (●); **`SK.12.Core` re-closed 24/24 (●)
same day** — `ClaimMappingOptions`, `IdentityKind`, `Permissions`/`HasPermission`, `SystemUserContext`, and
the full `SharedKernel.Security.ApiKey` runtime all shipped. `SK.12.Tests`/`Docs`/`Published` remain open for
WO-057. Root `state-map.md`'s domain-summary-board Current Phase column is deliberately left at `Published`
(not reverted to the in-progress phase) on EVERY phase-key promotion in this work order, including Core —
matches the `06.Persistence`/`07.Messaging`/`09.Search`/`11.Communication`/`13.ServiceDefaults` precedent: a
domain that already reached Published never regresses its Current Phase column just because a later work
order reopens earlier phase keys for extension. Confirmed this pattern holds for `Core`, not just
`Design`/`Scaffold` — the precedent generalizes to every standard lifecycle phase key.

**WO-058 (2026-08-13):** confirms the "Design phase = pure specification, verification-only" pattern
generalizes beyond WO-057 — `SK.12.Design` re-closed 34/34 (●) purely by re-reading `12.Security/CLAUDE.md`
and confirming all 12 remaining tasks (D-23–D-34: step-up auth surface, DPoP, `.Mtls` package shape, token
revocation seam) were already fully specified by `security-arch-planner`'s dispatch — zero `.cs`/`.md` edits
needed. Also confirms the "root Domain Summary Board Current Phase stays `Published`, only `State` regresses
to `◐`" precedent holds a third time (now proven across WO-057's Design/Scaffold/Core AND WO-058's Design).

## Cross-domain `IUserContext` interface-change fallout (WO-057, 2026-08-13)

Adding members to `IUserContext` (`IdentityKind`, `Permissions`, `HasPermission`) breaks EVERY other
implementer of that interface repo-wide, not just the ones inside `12.Security`. Found via
`Grep(pattern: "class\s+\w+(<[^>]+>)?\s*(\([^)]*\))?\s*:\s*[\w<>,\.\s]*\bIUserContext\b", glob: "*.cs")` —
this pattern catches implementers regardless of interface-list position (single or multi-interface). Found
and fixed 4 implementers OUTSIDE `12.Security`:
- `06.Persistence/SharedKernel.Persistence.EfCore/Extensions/NoOpUserContext.cs` (production no-op fallback)
- `06.Persistence/.../SharedKernel.Persistence.EfCore.Tests/Extensions/EfCorePersistenceBuilderTests.cs`
  (`CustomUserContext` test fixture)
- `06.Persistence/.../SharedKernel.Persistence.EfCore.Tests/Extensions/DbContextPoolingTests.cs`
  (`MutableTestUserContext` internal test fixture)
- `16.Testing/SharedKernel.Testing/Security/FakeUserContext.cs` (public fake, `IdentityKind`/`Permissions`
  made settable per `16.Testing/CLAUDE.md`'s own already-documented target design — that domain's brain had
  ALREADY speced this exact addition as `⚑ Blocked pending 12.Security's SK.12.Core` under tasks C-111–C-113;
  implementing it here unblocks that domain's own future session, though its state-map/CLAUDE.md were
  deliberately left untouched — out of `12.Security`'s jurisdiction)
- `16.Testing/SharedKernel.Testing/Persistence/TestSharedKernelDbContext.cs` (private nested `NoOpUserContext`)

**Rule for future interface-breaking changes to a foundational cross-domain contract**: after making the
change, ALWAYS grep the whole repo for implementers before declaring the phase done — `dotnet build` on just
the owning domain's own projects will NOT catch these; only a full-solution build (or the targeted grep) does.
A full `dotnet build Platform.SharedKernel.slnx` also works but is slow and mixes in unrelated pre-existing
NuGet version-resolution errors (e.g. `02.Caching`'s test projects have had `NU1605` Polly/DI-version-downgrade
errors unrelated to any Security change — confirmed pre-existing, not introduced this session); filtering
build output for `error CS` (not just `error`) isolates genuine compile errors from NuGet restore noise.

## Pack notes

- NuGet metadata was present from the Scaffold phase — no .csproj edits needed in Published
- NU1903 warnings on `System.Security.Cryptography.Xml` 9.0.0 are transitive from `Microsoft.Identity.Web` — cannot be suppressed without removing the package; not a code defect
- `dotnet pack` outputs to `artifacts/nupkg/` (created on first pack run)

## SharedKernel.Security.ApiKey scaffold notes (WO-057, 2026-08-13)

- **`Microsoft.AspNetCore.App` FrameworkReference, not a NuGet PackageReference.** `ApiKeyAuthenticationHandler`
  needs `AuthenticationHandler<TOptions>` (`Microsoft.AspNetCore.Authentication.Abstractions`) and `[LoggerMessage]`
  needs `Microsoft.Extensions.Logging.Abstractions` — both ship inside the ASP.NET Core shared framework. A
  `<FrameworkReference Include="Microsoft.AspNetCore.App" />` satisfies both without violating the "no other
  third-party NuGet dependencies" scaffold constraint (`FrameworkReference` ≠ `PackageReference`) — this is
  exactly what `12.Security/CLAUDE.md`'s Technology Stack table means by "framework-provided, not a NuGet
  dependency" for that row.
- **A `.Tests` project that references `16.Testing/SharedKernel.Testing` must pin
  `Microsoft.Extensions.DependencyInjection`/`.Hosting` to `10.0.9`, not the `9.0.5` `SharedKernel.Security.Oidc.Tests`
  uses.** `SharedKernel.Testing` itself pulls `Microsoft.Extensions.DependencyInjection >= 10.0.9` transitively;
  an explicit lower pin in the referencing `.Tests` project produces an `NU1605` downgrade error (treated as
  error, not warning, in this repo's restore). Always check what `SharedKernel.Testing.csproj` currently pins
  before choosing a test-project package version, rather than copying a sibling `.Tests` project's versions
  blindly.
- **Scaffold-phase empty test stubs use `[Fact(Skip = "...")]` with a reason naming the blocking phase/task
  IDs**, not a bare empty class — keeps `dotnet test` green (skipped, not failing) while the referenced
  production type doesn't exist yet.
- **`Logging/SecurityLogEvents.cs` Scaffold stub = a bare `internal static partial class` with only an XML
  `<remarks>` documenting the reserved `EventId` sub-range** (`.Oidc` = 12100-12199, `.ApiKey` = 12200-12299,
  both within `01.Core`'s `LoggingEventIdRanges.Security` = 12000-12999 block) — no `[LoggerMessage]`-attributed
  partial method declarations yet; those are added in Core (C-24), since source-generated partial methods
  have no body for a human to "leave empty" — the whole method declaration is deferred, not just its body.

## WO-058 Scaffold phase (SC-16–SC-24, 2026-08-13)

- **Scaffold-only phases do not warrant `sync-brain`.** Confirmed by re-reading `12.Security/CLAUDE.md`'s
  own Changelog: WO-057's Scaffold phase (SC-10–SC-15) never produced a distinct "SK.12.Scaffold closed"
  brain-sync changelog line — only Design (dispatch), Tests, Docs, and Core phases did. The reason: when
  `security-arch-planner` dispatches a work order, it writes the FULL target-state Interface Contracts
  into CLAUDE.md up front (marked `design-locked`) — Scaffold only wires plumbing (csproj refs, folders,
  solution registration, stub files) against an already-fully-specified brain, so there is nothing for a
  brain sync to correct. Skipped again this session for the identical reason.
- **"Stub file" in a Scaffold task has three different legitimate shapes, not one**, depending on what the
  named type IS:
  - **A pure-contract type (interface, POCO options class) whose shape was already fully finalized in the
    Design phase** → write it FULLY now. An interface's method signature and an options class's properties
    carry zero "logic" (logic = imperative implementation code) — deferring them to Core would just be
    busywork churn, not genuine phase discipline. Example: `IDpopProofReplayCache`,`ITokenRevocationCheck`
    (single-method interfaces, D-28/D-33 already pinned their exact signatures), `DpopOptions` (a bare
    `int ProofFreshnessWindowSeconds { get; set; } = 60`).
  - **A `[LoggerMessage]`-attributed partial method whose EventId/Level/Message is already published in
    CLAUDE.md's Logging Conventions table** → declare it now too. The attribute is 100% declarative; the
    source generator produces the entire method body, so there is no hand-written logic to defer. This is
    what "method skeleton" means in a Scaffold task when the surrounding phase spec explicitly asks for a
    `[LoggerMessage]` skeleton (not just "no bodies yet" like the original SC-15 stub) — check the task's
    exact wording, don't assume the SC-15 precedent generalizes verbatim.
  - **A class whose entire purpose IS an algorithm/imperative logic** (e.g. `DpopProofValidator` — JWT
    parsing, signature verification, freshness checks) → leave as a doc-only empty class body with an XML
    `<remarks>` naming the future Core task ID that will fill it in. This is the one case where "no logic
    yet" is taken literally.
- **A brand-new sibling provider package's csproj should copy the immediately-preceding sibling's csproj
  almost verbatim**, not re-derive patterns from first principles. `SharedKernel.Security.Mtls.csproj` was
  built directly off `SharedKernel.Security.ApiKey.csproj`: same `<FrameworkReference
  Include="Microsoft.AspNetCore.App" />` trick for a framework-provided auth handler
  (`Microsoft.AspNetCore.Authentication.Certificate` this time, not `.Abstractions`) instead of a
  `PackageReference` — even though `12.Security/CLAUDE.md`'s own Technology Stack table literally reads
  "NuGet: Microsoft.AspNetCore.Authentication.Certificate", the accompanying prose ("framework-provided
  handler") is the authoritative signal, and the table's "NuGet" column header is naming the *capability*
  supplier, not literally mandating a `PackageReference` element. Same `.Tests.csproj` package-version
  pins (xunit 2.9.3, Microsoft.NET.Test.Sdk 17.13.0, `Microsoft.Extensions.DependencyInjection`/`.Hosting`
  pinned to `10.0.9` because the project references `16.Testing/SharedKernel.Testing`, which itself pulls
  that floor transitively — a lower pin here is an `NU1605` downgrade error, same as `.ApiKey.Tests`).
- **Scaffold-phase skipped test stubs for a brand-new package use `[Fact(Skip = "...")]` naming the
  specific blocking Core-phase task IDs** (e.g. `"...awaiting IMtlsCertificateValidator/MtlsUserContext/
  certificate authentication handler (SK.12.Core C-32/C-33/C-34)."`), never a bare empty test class —
  keeps `dotnet test` reporting 0 failed/N skipped instead of 0 tests found, and gives the next session's
  implementer a direct pointer to which Core task closes each stub.
- **`ClaimMappingOptions`/an existing options class extended with new properties mid-domain is genuinely a
  Scaffold task, not Core** — properties-with-defaults are declarative data, so adding
  `AmrClaimType`/`AcrClaimType`/`AuthTimeClaimType` (defaults `"amr"`/`"acr"`/`"auth_time"`) to the
  already-shipped `ClaimMappingOptions` belonged in Scaffold (SC-16); the Core-phase counterpart (C-27) is
  specifically the *resolution logic* in `OidcUserContext` that reads these new properties — the split is
  clean and mirrors the interface-vs-implementation distinction above. Avoid `<see cref>` references from
  a Scaffold-phase XML doc to `IUserContext` members that don't exist yet (e.g. `AuthenticationMethods`
  before C-25 ships) — produces an unresolvable-cref warning; describe in prose instead until the member
  is real.

## SharedKernel.Security.ApiKey Core-phase design decisions (WO-057, 2026-08-13)

- **Composing `IUserContext` across sibling provider packages without a cross-reference.** `ApiKey` cannot
  reference `Oidc` (siblings never reference each other, `12.Security/CLAUDE.md` hard rule), yet
  `AddApiKeyAuthentication` must make `IUserContext` resolve to `ApiKeyUserContext` for API-key-authenticated
  requests and fall through to whatever `AddSharedKernelSecurity` already registered (`OidcUserContext`) for
  everything else. Solved via a **decorator built from `ServiceDescriptor` capture**: before registering its
  own scoped `IUserContext` factory, `AddApiKeyAuthentication` does
  `services.LastOrDefault(d => d.ServiceType == typeof(IUserContext))` to grab whatever was already
  registered (typically by `AddSharedKernelSecurity`, called first per the documented usage order), then its
  new factory checks `HttpContext.User.Identity?.AuthenticationType == ApiKeyAuthenticationOptions
  .DefaultScheme` — if true, builds `ApiKeyUserContext` directly; if false, invokes the captured
  `ServiceDescriptor.ImplementationFactory(sp)` (or falls back to `AnonymousUserContext.Instance` if nothing
  was captured). This works because .NET DI resolves the LAST-registered descriptor for a given service type
  — registering a second `IUserContext` factory after capturing the first doesn't remove it, just shadows it
  for direct resolution while remaining reachable via the captured `ServiceDescriptor` reference. Generalizes
  to any future "compose with whatever a sibling package registered, without referencing that package" need.
- **Constant-time comparison — figure out WHAT is actually being compared before reaching for
  `IHmacSigner`.** `IApiKeyValidator` (consumer-supplied) owns the real presented-key-vs-stored-secret
  comparison, entirely opaque to this package — there's no way for `ApiKeyAuthenticationHandler` to
  constant-time-compare something it never holds both sides of. The genuine, defensible use for
  constant-time comparison INSIDE this package: when a key is presented via BOTH the header and a configured
  query parameter on the same request, comparing those two PRESENTED values against each other (to detect
  a credential-confusion attack) is something the Handler genuinely CAN do, since it holds both operands.
  Built `ConstantTimeKeyComparer` (internal) on `SharedKernel.Cryptography.Signing.IHmacSigner`: HMAC both
  values under a fixed local pepper and compare the two MACs via `IHmacSigner.Verify` (which internally uses
  `CryptographicOperations.FixedTimeEquals`) — never a raw `FixedTimeEquals` call directly, since the brief
  said "delegate to `SharedKernel.Cryptography`'s existing primitives," and `IHmacSigner` is the closest
  existing primitive that composes into a constant-time equality check. **Lesson: when a spec says "constant
  time comparison here" but the natural place to put it has no direct access to the secret being compared,
  look for what the code AT THAT LOCATION genuinely holds both sides of — don't force a comparison that
  doesn't structurally make sense there.**
- **Testing a custom `AuthenticationHandler<TOptions>` without `WebApplicationFactory`.** Construct the
  handler directly (`new ApiKeyAuthenticationHandler(optionsMonitor, NullLoggerFactory.Instance,
  UrlEncoder.Default, validator, hmacSigner, logger)`), then call the base class's public
  `InitializeAsync(new AuthenticationScheme(name, null, typeof(Handler)), new DefaultHttpContext())` followed
  by `AuthenticateAsync()` — both are public members of `AuthenticationHandler<TOptions>` (implementing
  `IAuthenticationHandler`), no `TestServer`/`WebApplicationFactory` needed. Needs a hand-rolled
  `IOptionsMonitor<TOptions>` fake (`CurrentValue`/`Get(name)`/`OnChange` — trivial, ~10 lines). Set the
  request header via `httpContext.Request.Headers["X-Api-Key"] = "value"`; set the query string via
  `httpContext.Request.QueryString = QueryString.Create("api_key", "value")`. `16.Testing`'s
  `InMemoryLogger<T>` works as the `ILogger<THandler>` constructor argument for asserting on
  `[LoggerMessage]` output.

## WO-058 Core phase (SK.12.Core, C-25–C-37, 2026-08-14) — step-up auth, DPoP, mTLS, revocation

- **`Microsoft.AspNetCore.Authentication.Certificate` is NOT part of the `Microsoft.AspNetCore.App` shared
  framework — confirmed by listing the actual installed shared-framework directories on disk, not by
  trusting the pre-written domain brain.** `12.Security/CLAUDE.md`'s Technology Stack table had this row
  labeled "framework-provided handler," inherited verbatim from the `.ApiKey`/`Microsoft.AspNetCore
  .Authentication.Abstractions` precedent (which genuinely IS framework-provided). The `Certificate` auth
  middleware is a separate, real NuGet package that must be added via `<PackageReference
  Include="Microsoft.AspNetCore.Authentication.Certificate" Version="10.0.0" />` — the `<FrameworkReference
  Include="Microsoft.AspNetCore.App" />` alone does not resolve `CertificateAuthenticationOptions`/
  `CertificateValidatedContext`/etc. **Lesson: when a design doc says "framework-provided" for a specific
  ASP.NET Core sub-area (not the base `Http`/`DependencyInjection`/`Authentication.Abstractions` surface),
  verify against the real installed SDK (`find "$(dirname $(which dotnet))/shared/Microsoft.AspNetCore.App"
  -iname "*Xyz*"`) before writing the csproj — do not assume every `Microsoft.AspNetCore.*`-namespaced type
  ships in the shared framework just because some do.**

- **Cross-request signal bridging via a synthetic marker claim.** DPoP proof validation (RFC 9449) happens
  inside `JwtBearerEvents.OnTokenValidated`, but `IUserContext.IsSenderConstrained` is read much later, when
  `OidcUserContext` is constructed by the DI `IUserContext` factory from the (by-then-already-validated)
  `ClaimsPrincipal`. Bridged by stamping an internal, never-emitted-by-any-real-IdP marker claim
  (`SharedKernel.Security.Oidc.Dpop.DpopClaimTypes.SenderConstrained = "sk_dpop_bound"`) onto
  `context.Principal`'s `ClaimsIdentity` inside the validator, on success only; `OidcUserContext`'s
  constructor just checks for its presence. **Generalizes to any future signal that must survive from
  token-validation-time to `IUserContext`-construction-time without adding a new constructor parameter or
  widening the `ClaimsPrincipal`-only construction contract** — cheaper than plumbing an `HttpContext.Items`
  side-channel, and travels naturally with the principal through any code that re-reads it.

- **Root-vs-scoped `IServiceProvider` captive-dependency trap inside `services.AddOptions<TOptions>(...)
  .Configure(o => {...})`.** The delegate passed to `.Configure(...)` runs once, using whatever
  `IServiceProvider` `IOptionsMonitor`'s internal machinery resolves it with — effectively the ROOT
  container, not a per-request scope. Resolving a `Scoped`-lifetime service (e.g. `IDpopProofReplayCache`,
  `ITokenRevocationCheck`) from a provider captured at that point either throws (scope-validation enabled)
  or silently returns a container-lifetime-pinned instance. **Fix: never resolve scoped seam dependencies
  from an `IServiceProvider` captured by the `Configure` delegate's closure. Resolve them from
  `context.HttpContext.RequestServices` INSIDE the per-request `OnTokenValidated`/event-handler delegate
  body instead** — that delegate runs per-request with the real scoped container. This is the one AOT/DI
  gotcha in this domain worth checking on every future `.Configure<JwtBearerOptions>()`-based extension.

- **Wrap-previous-handler chaining pattern for composable `JwtBearerEvents.OnTokenValidated` extensions.**
  `SecurityAuthenticationBuilder.RequireDpop<TReplayCache>()`/`.WithRevocationCheck<TCheck>()` both do:
  ```csharp
  services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).Configure(o => {
      o.Events ??= new();
      var previous = o.Events.OnTokenValidated;
      o.Events.OnTokenValidated = async ctx => {
          if (previous is not null) await previous(ctx);
          if (ctx.Result is not null) return;   // an earlier handler already failed the request
          // ...this seam's own validation, resolved from ctx.HttpContext.RequestServices...
      };
  });
  ```
  Calling `.RequireDpop()` then `.WithRevocationCheck()` composes both in call order without either knowing
  the other exists — each just wraps whatever was there before. `ctx.Result is not null` is the short-circuit
  that stops a later handler from overriding an earlier rejection. Reusable for any future opt-in
  `JwtBearerEvents` extension in this domain (e.g. a future custom claims-enrichment step).

- **A builder that must both configure DI (fluent chain) and remain 100% `IServiceCollection`-compatible for
  every OTHER extension method in the ecosystem** (`AddAuthorization()`, `AddHttpContextAccessor()`, etc.) is
  solved by making the builder itself implement `IServiceCollection`, forwarding every member to a wrapped
  instance (`SecurityAuthenticationBuilder : IServiceCollection`). Changing `AddSharedKernelSecurity`'s return
  type from `IServiceCollection` to this builder is **source-compatible, not breaking** — any existing caller
  that only chains further `IServiceCollection` extension methods keeps compiling, because the builder IS an
  `IServiceCollection`. Worth reusing as the default shape whenever a domain needs "fluent builder +
  full IServiceCollection passthrough" instead of forcing callers into a narrower builder-only API.

- **`Microsoft.IdentityModel.Tokens.JsonWebKey` ships a built-in RFC 7638 JWK thumbprint
  (`.ComputeJwkThumbprint()`) and `Base64UrlEncoder.Encode/.Decode`** — both already transitively available
  via the `JwtBearer`/`Microsoft.Identity.Web` NuGet chain `SharedKernel.Security.Oidc.csproj` already
  references, so implementing DPoP's `jkt` computation needed ZERO new top-level `PackageReference`. To parse
  a DPoP proof's embedded `jwk` header (a nested JSON object, not a simple claim): base64url-decode
  `JsonWebToken.EncodedHeader`, parse with `JsonDocument`, extract the raw `"jwk"` property text, feed it
  directly to `JsonWebKey.Create(json)` — simpler than trying to coerce `JsonWebToken.TryGetHeaderValue<T>`
  into a nested-object shape. Self-signed-by-embedded-key proof verification is a legitimate narrow use of
  `JsonWebTokenHandler.ValidateTokenAsync(proof, new TokenValidationParameters { ValidateIssuer=false,
  ValidateAudience=false, ValidateLifetime=false, IssuerSigningKey = jwk })`.

- **Building a real signed DPoP proof JWT in tests** (no mocking needed — genuine crypto):
  `ECDsa.Create(ECCurve.NamedCurves.nistP256)` → `JsonWebKeyConverter.ConvertFromECDsaSecurityKey(new
  ECDsaSecurityKey(ecdsa))` for the Kty/Crv/X/Y components → `JsonWebTokenHandler.CreateToken(new
  SecurityTokenDescriptor { SigningCredentials = ..., Claims = {htm, htu, iat, jti}, AdditionalHeaderClaims =
  {typ, jwk = {kty, crv, x, y}} })`. Reusable recipe for any future test needing a real signed JWT with an
  embedded JWK header.

- **A third package now shares the `InternalsVisibleTo` → `.Tests` pattern** (`.ApiKey` →
  `ConstantTimeKeyComparer`, `.Oidc` → `DpopProofValidator`/`RevocationCheckRunner`/`DpopClaimTypes`, `.Mtls`
  → `MtlsAuthenticationHandler`/`ConstantTimeThumbprintComparer`/`MtlsClaimTypes`) — confirmed as the
  established, repeatable convention for testing `internal` validator/handler classes directly in this
  domain, not a one-off.

- **Testing `CertificateAuthenticationEvents.OnCertificateValidated` (mTLS) without a real TLS handshake.**
  Build a `CertificateValidatedContext(httpContext, scheme, options) { ClientCertificate = cert }` directly —
  `CertificateAuthenticationHandler` itself is `internal` to the framework package and cannot be referenced
  from a test project, so use a throwaway local `IAuthenticationHandler`-implementing stand-in class (e.g.
  `NoOpAuthenticationHandler`) purely to satisfy `AuthenticationScheme`'s `handlerType` constructor argument
  — it is never actually invoked by these unit tests. Self-signed test certs via `RSA.Create(2048)` +
  `CertificateRequest(...).CreateSelfSigned(...)`. For an RFC 8705 `cnf.x5t#S256` claim, build the JSON
  directly as an interpolated string (`$"{{\"x5t#S256\":\"{thumbprint}\"}}"`) rather than
  `JsonSerializer.Serialize` + `.Replace(...)` — C# property names can't contain `#`, so serializing a POCO
  and string-replacing the property name afterward is a needless hack; a literal JSON string is clearer.

- **Cross-domain `IUserContext` interface-change fallout, reconfirmed a second time (first was WO-057).**
  Adding six more `IUserContext` members again required fixing implementers OUTSIDE `12.Security`:
  `06.Persistence.EfCore/Extensions/NoOpUserContext.cs`, two of that package's own test fixtures
  (`EfCorePersistenceBuilderTests.CustomUserContext`, `DbContextPoolingTests.MutableTestUserContext`),
  `16.Testing/SharedKernel.Testing/Security/FakeUserContext.cs` (gains matching SETTABLE properties, per its
  established convention — sentinels in production fallbacks, mutable fields in the public test fake), and
  `16.Testing/.../Persistence/TestSharedKernelDbContext.cs`'s private nested `NoOpUserContext`. The
  repo-wide grep pattern from WO-057's memory entry (`class\s+\w+(<[^>]+>)?\s*(\([^)]*\))?\s*:\s*[\w<>,\.\s]
  *\bIUserContext\b`) again caught every implementer correctly — **this rule is now confirmed reliable across
  two independent interface-breaking sessions; keep using it (re-derive fresh each time, never trust a prior
  session's list as still-exhaustive) whenever `IUserContext` or `ITenantProvider` gains a member.**
