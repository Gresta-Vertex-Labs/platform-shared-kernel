---
name: kestrel_private_delegate_reflection_technique
description: How to genuinely prove KestrelServerOptions.ConfigureHttpsDefaults(...) wiring in a unit test — reflection over a private property, confirmed via decompilation, no real TLS handshake needed
metadata:
  type: project
---

**Problem this solves:** `AddMtlsClientCertificate` (WO-058/P-378, `Security/MtlsClientCertificateExtensions.cs`)
wires Kestrel's `ClientCertificateMode`/`ClientCertificateValidation` via
`services.AddOptions<KestrelServerOptions>().Configure<IServiceScopeFactory>((kestrel, scopeFactory) =>
kestrel.ConfigureHttpsDefaults(https => { ... }))`. Kestrel exposes **no public API** to read back what a
`ConfigureHttpsDefaults(...)` call registered — a genuine delegation proof (not merely "the extension method
didn't throw") needs a way to actually invoke the wired delegate against a real `HttpsConnectionAdapterOptions`.

**Empirical finding (decompiled `Microsoft.AspNetCore.Server.Kestrel.Core.dll` via `ilspycmd -t
Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions "<sdk-shared-framework-path>/Microsoft.AspNetCore.Server.Kestrel.Core.dll"`,
version `10.0.10` under `C:\Program Files\dotnet\shared\Microsoft.AspNetCore.App\`):**
`ConfigureHttpsDefaults(Action<HttpsConnectionAdapterOptions> configureOptions)` does **not** append to a list —
it's a one-line assignment: `private Action<HttpsConnectionAdapterOptions> HttpsDefaults { get; set; } =
delegate {}; public void ConfigureHttpsDefaults(Action<...> configureOptions) { HttpsDefaults = configureOptions
?? throw new ArgumentNullException(...); }`. The internal `ApplyHttpsDefaults(HttpsConnectionAdapterOptions)`
method (also internal, invoked by Kestrel itself at real TLS-handshake configuration time) just calls
`HttpsDefaults(httpsOptions)`. Default (never-configured) value is the empty `delegate {}` no-op — confirms the
no-op regression baseline too.

**The test technique:**
1. Build a real `IHost` via `Host.CreateApplicationBuilder()` + the extension method under test + `.Build()`.
2. Resolve `host.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value` — this **forces** the
   `Configure<IServiceScopeFactory>` delegate registered by the extension to actually run (lazy `IOptions<T>`
   resolution triggers all registered `IConfigureOptions<T>`/`IConfigureNamedOptions<T>` against the shared
   instance).
3. Reflect over the private property: `typeof(KestrelServerOptions).GetProperty("HttpsDefaults",
   BindingFlags.NonPublic | BindingFlags.Instance).GetValue(kestrelOptions)` — cast to
   `Action<HttpsConnectionAdapterOptions>`. No special reflection permission needed on modern .NET (no CAS).
4. Invoke it against a fresh `new HttpsConnectionAdapterOptions()` — this reproduces exactly what Kestrel does
   internally at real handshake time, without needing an actual TLS connection.
5. Assert `.ClientCertificateMode` and invoke `.ClientCertificateValidation!(certificate, null,
   SslPolicyErrors.None)` directly to prove the accept/reject decision is dictated by the injected
   `IMtlsCertificateValidator` test double, not any independent chain-validation path.

**`HttpsConnectionAdapterOptions` decompiled defaults** (same assembly): constructor sets
`ClientCertificateMode = ClientCertificateMode.NoCertificate`; `ClientCertificateValidation` starts `null`. This
is the exact no-op-regression baseline to assert against for a host that never calls
`AddMtlsClientCertificate`/`AddMtlsForwardedHeaderCertificate`.

**Strongest genuine-delegation proof shape used (T-44):** run the SAME certificate through TWO separately-built
hosts — one with an accepting validator double, one with a rejecting one — and assert the wired
`ClientCertificateValidation` delegate's boolean output flips exactly with the validator's own decision. This is
stronger than a single accept-only or reject-only test because it rules out any hidden independent chain/subject/
issuer check that could happen to agree with the validator by coincidence on one path but not the other.

**Regression-proof discipline applied (per this domain's T-39/T-40/T-41/T-43 established pattern):** temporarily
removed the `https.ClientCertificateValidation = ...` assignment from production code — 4 tests genuinely failed
with `NullReferenceException` (delegate was null). Also removed `context.Connection.ClientCertificate =
certificate;` from `MtlsForwardedHeaderMiddleware` — 2 tests failed. Also removed the
`.Validate(...).ValidateOnStart()` chain from `MtlsForwardedHeaderExtensions` — 4 tests failed (including the
`RegistersAnOptionsValidator` DI-shape test). All three reverted with `git diff` confirming zero net change
before final commit.

See also [[decompile_verification_technique]] for the general `ilspycmd` workflow this session reused, and
[[otel_wiring_pattern]]/[[grpc_polly_test_capture_pattern]] for this domain's other genuine-capture-proof test
idioms (`BaseProcessor<Activity>` for OTel spans — a different technique, since Kestrel has no `Activity`-based
observability hook for this particular wiring).
