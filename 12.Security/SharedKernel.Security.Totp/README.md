# SharedKernel.Security.Totp

Second-factor (TOTP) enrollment and challenge orchestration for the SharedKernel, plus the ASP.NET Core-facing step-up wiring that makes a successful TOTP verification observable through `IUserContext.WasAuthenticatedWith`/`AuthenticationMethods` with **zero changes** to `SharedKernel.Security.Oidc` or `14.Presentation`'s `[RequireAuthenticationMethod]`. A fifth sibling provider package alongside `.Oidc`/`.ApiKey`/`.Mtls` — never a dependent of any of them, and never referenced by them.

> **This package never reimplements RFC 6238/4226.** Every TOTP/HOTP primitive — secret generation, Base32 encoding, provisioning-URI construction, drift-windowed code validation, and replay protection — is delegated entirely to `01.Core/SharedKernel.Cryptography`'s `ITotpGenerator`/`ITotpVerifier`/`TotpSecret`/`Base32`/`TotpProvisioningUri`/`IRecoveryCodeGenerator`. This package only orchestrates those primitives and wires the result into ASP.NET Core.

## Public Surface

| Type | Kind | Purpose |
| --- | --- | --- |
| `TotpEnrollment` | Sealed record | The secret, its encodings, the `TotpParameters` it was enrolled with, and one-time recovery codes produced by a new enrollment |
| `TotpEnrollmentService` | Sealed class | Generates a new `TotpEnrollment` — synchronous, side-effect-free, never persists anything |
| `ITotpChallengeStore` | Interface | The sole consumer-supplied extensibility point — tracks the most recent successful TOTP challenge per identity |
| `TotpChallengeService` | Sealed class | Verifies a presented code (or records an out-of-band recovery-code step-up), composing `01.Core`'s `ITotpVerifier` and returning its `TotpVerificationResult` |
| `TotpStepUpOptions` | Sealed class | The AMR claim type/value and freshness window applied by the step-up transformation |
| `TotpStepUpClaimsTransformation` | `IClaimsTransformation` | Stamps the configured AMR claim onto the current principal after a fresh successful challenge |
| `TotpServiceCollectionExtensions` | Static class | `AddTotpStepUp<TChallengeStore>` extension on `01.Core`'s `ICryptographyBuilder` |

## How the step-up mechanism works

ASP.NET Core invokes every registered `IClaimsTransformation` during `AuthenticateAsync`, **before** any `IUserContext` DI factory first resolves for the request. `SharedKernel.Security.Oidc`'s existing defensive `amr`-claim reader (`OidcUserContext`, shipped alongside step-up authentication support) already reads every `amr` claim on the principal — so a claim `TotpStepUpClaimsTransformation` stamps here is picked up with **zero code change in `.Oidc` itself**. This mirrors the same sanctioned "stamp a marker claim during/after authentication, read it generically downstream" mechanism this domain already uses for DPoP's `IsSenderConstrained`, applied via a framework-provided, scheme-agnostic hook instead of a `JwtBearerEvents.OnTokenValidated` hook (which only `.Oidc` itself may register, since only `.Oidc` owns `JwtBearerOptions`).

```
 1. User authenticates normally (JWT Bearer via .Oidc) — a ClaimsPrincipal with a "sub" claim exists.
 2. User separately completes a TOTP challenge -> TotpChallengeService.VerifyAsync(userId, secret, code)
      -> records success in ITotpChallengeStore, keyed by the user id.
 3. On the NEXT request, ASP.NET Core's authentication pipeline runs every IClaimsTransformation,
    including TotpStepUpClaimsTransformation, BEFORE IUserContext is resolved:
      -> finds the fresh challenge in ITotpChallengeStore
      -> stamps an "amr": "otp" claim onto a NEW ClaimsIdentity (never mutates the original)
 4. .Oidc's OidcUserContext constructor (unchanged) reads that claim into AuthenticationMethods.
 5. Application code calls user.WasAuthenticatedWith("otp") -- true, with zero .Oidc changes.
```

**Scope boundary:** this composes only with `.Oidc`'s `OidcUserContext` — the human end-user path, the only implementer that reads `amr` claims. It has no effect on `.ApiKey`/`.Mtls` primary identities, which never carry a parseable `sub` claim (always `UserId == Guid.Empty`) — the guard inside `TotpStepUpClaimsTransformation` skips them structurally, not by special-casing those packages.

**Never touches `AuthTime`.** A TOTP step-up only augments `AuthenticationMethods`/`WasAuthenticatedWith` — `[RequireFreshAuthentication(maxAge)]` stays keyed solely on the *primary* authentication's own recency; `[RequireAuthenticationMethod("otp")]` is the correct, independent gate for "was TOTP verified," and its own freshness is enforced entirely by `TotpStepUpOptions.ChallengeFreshnessWindow` at claim-stamping time. The two gates are deliberately never conflated.

## DI Registration

Implement `ITotpChallengeStore` against whatever storage your service already uses (in-memory for a single-instance dev host, Redis/database for a production multi-replica deployment — this package never dictates it):

```csharp
public sealed class DatabaseTotpChallengeStore(ITotpChallengeRepository repository) : ITotpChallengeStore
{
    public Task RecordSuccessfulChallengeAsync(string identityKey, DateTimeOffset verifiedAt, CancellationToken ct = default) =>
        repository.UpsertLastChallengeAsync(identityKey, verifiedAt, ct);

    public Task<DateTimeOffset?> TryGetLastSuccessfulChallengeAsync(string identityKey, CancellationToken ct = default) =>
        repository.FindLastChallengeAsync(identityKey, ct);
}
```

Register **after** `AddSharedKernelSecurity`/`AddAzureB2CAuthentication` (so an authenticated JWT Bearer principal exists for the transformation to observe). `AddTotpStepUp<TChallengeStore>` chains onto the builder returned by `01.Core`'s `AddSharedKernelCryptography`, which registers `ITotpGenerator`/`IRecoveryCodeGenerator`/`ISecureRandomGenerator`; it adds `ITotpVerifier` itself (via `.AddTotpVerification()`). Your service supplies the `ITotpReplayGuard`, as a singleton over a store every replica shares:

```csharp
services.AddSharedKernelSecurity(configuration);

// Your own ITotpReplayGuard — 01.Core ships no default implementation. It must compare-and-store the
// accepted time step atomically (a Lua script, a conditional UPDATE); see ITotpReplayGuard's XML docs.
services.AddSingleton<ITotpReplayGuard, RedisTotpReplayGuard>();

services.AddSharedKernelCryptography(configuration)
    .AddTotpStepUp<DatabaseTotpChallengeStore>(options =>
    {
        options.ChallengeFreshnessWindow = TimeSpan.FromMinutes(15); // default
    });
```

`AddTotpStepUp<TChallengeStore>` is **not** chained onto `SecurityAuthenticationBuilder` — that type is owned by `.Oidc`, and this package cannot reference `.Oidc` under the sibling-packages-never-reference-each-other rule. Chaining it onto `01.Core`'s `ICryptographyBuilder` instead makes the cryptography registration impossible to forget.

## Enrollment recipe

`TotpEnrollmentService.GenerateEnrollment` returns everything needed to show a user a QR code and a one-time set of recovery codes — but **never persists any of it**. Encrypting the raw secret at rest and hashing each recovery code at rest is entirely your own responsibility. Store `enrollment.Parameters` too when you enroll with anything other than `TotpParameters.Default`: authenticator apps keep generating codes with the digits, period and algorithm they scanned.

```csharp
public sealed class EnrollUserInTotp(TotpEnrollmentService enrollmentService, ISymmetricEncryptionService encryption, IOneWayHasher hasher)
{
    public async Task<EnrollmentViewModel> EnrollAsync(Guid userId, string userEmail, CancellationToken ct)
    {
        TotpEnrollment enrollment = enrollmentService.GenerateEnrollment(issuer: "Contoso", accountName: userEmail);

        // Encrypt the raw secret before it ever reaches storage — never store it plaintext. Binding the user id
        // as associated data stops one user's ciphertext being copied onto another user's row.
        EncryptedPayload encryptedSecret = await encryption.EncryptAsync(enrollment.Secret, userId.ToByteArray(), ct);

        // Hash each recovery code, in normalized form, before it ever reaches storage — a recovery code IS a
        // secret, exactly like a password or API key. Normalizing lets "k7q2mxf4pa" match "K7Q2M-XF4PA" later.
        var hashedRecoveryCodes = enrollment.RecoveryCodes
            .Select(code => hasher.Hash(RecoveryCodeGenerator.Normalize(code)))
            .ToList();

        // Your own storage.
        await SaveEnrollmentAsync(userId, encryptedSecret.ToString(), enrollment.Parameters, hashedRecoveryCodes, ct);

        // Show enrollment.ProvisioningUri as a QR code and enrollment.RecoveryCodes to the user
        // EXACTLY ONCE, here, in this response — never log them, never persist the plaintext forms.
        return new EnrollmentViewModel(enrollment.ProvisioningUri, enrollment.RecoveryCodes);
    }
}
```

## Challenge recipe

A worked, non-production reference implementation of both a primary-code challenge and a recovery-code fallback ships inside this package at `Samples/TotpChallengeRecipeSample.cs`, exercised by this package's own test suite — read it alongside this recipe rather than as a drop-in replacement for your own integration code.

```csharp
public sealed class TotpChallengeEndpoint(TotpChallengeService challengeService, ITotpEnrollmentLookup enrollmentLookup, ITotpAttemptThrottle throttle)
{
    public async Task<IResult> VerifyPrimaryCodeAsync(Guid userId, string presentedCode, CancellationToken ct)
    {
        // RFC 4226 section 7.3 requires limiting attempts; neither TotpChallengeService nor ITotpVerifier does it.
        string identityKey = userId.ToString("D");
        if (await throttle.IsThrottledAsync(identityKey, ct))
        {
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        await throttle.RecordAttemptAsync(identityKey, ct);
        (byte[] decryptedSecret, TotpParameters parameters) = await enrollmentLookup.GetDecryptedSecretAsync(userId, ct);

        // TotpChallengeService.VerifyAsync composes 01.Core's ITotpVerifier (RFC 6238 validation plus
        // time-step replay protection) and, on Valid, records the challenge for the step-up transformation.
        return await challengeService.VerifyAsync(userId, decryptedSecret, presentedCode, parameters, ct) switch
        {
            TotpVerificationResult.Valid => Results.NoContent(),
            TotpVerificationResult.Replayed => Results.Problem("This code was already used. Wait for the next one.", statusCode: 400),
            _ => Results.Problem("The code is incorrect.", statusCode: 400),
        };
    }

    public async Task<bool> VerifyRecoveryCodeAsync(Guid userId, string presentedRecoveryCode, CancellationToken ct)
    {
        // Recovery-code matching is ENTIRELY your own concern -- TotpChallengeService is never
        // involved in it, only in recording that a step-up occurred once you've verified it yourself
        // (e.g. IOneWayHasher.Verify of RecoveryCodeGenerator.Normalize(presentedRecoveryCode) against
        // your own stored hashes).
        bool matched = await enrollmentLookup.TryConsumeRecoveryCodeAsync(userId, presentedRecoveryCode, ct);
        if (!matched)
        {
            return false;
        }

        await challengeService.RecordStepUpAsync(userId, ct);
        return true;
    }
}
```

## Security notes

- `ITotpChallengeStore`/`ITotpReplayGuard`/`ITotpAttemptThrottle` never dictate storage — the consumer-supplied extensibility points, mirroring `SharedKernel.Security.Oidc`'s `IDpopProofReplayCache`/`ITokenRevocationCheck` "never dictates storage" precedent exactly. This package never references `02.Caching` or `06.Persistence`.
- Replay protection is per time step, not per code: once a code is accepted, no code from the same or an earlier time step is accepted again for that user. `TotpChallengeService.VerifyAsync` reports such a code as `TotpVerificationResult.Replayed`, distinct from `Invalid`.
- `TotpChallengeService.VerifyAsync`/`.RecordStepUpAsync` and `TotpStepUpClaimsTransformation.TransformAsync` both hard-reject `Guid.Empty`/an anonymous principal **before** any store or verifier call — an empty identity is never a valid step-up subject. This is also what structurally excludes `.ApiKey`/`.Mtls` machine-credential identities from ever triggering a lookup.
- No log statement in this package ever includes a raw TOTP code, raw secret, or raw recovery code — only structured, safe fields (a failure-reason string, or nothing at all).
- This package never persists an enrollment's secret or recovery codes — encrypting/hashing them at rest is entirely your own responsibility (see the enrollment recipe above).
