---
name: project-core-domain
description: 01.Core implementation status — six packages, all Published as of 2026-06-26
metadata:
  type: project
---

The 01.Core domain contains six independently publishable packages, all fully `Published` as of 2026-06-26:
- `SharedKernel.Primitives` — Result<T>, Error, IClock, SmartEnum, ValidationResult
- `SharedKernel.Core` — Base exceptions, railway extensions, BCL helpers
- `SharedKernel.Guards` — Two-path guard system (Against.* functional, Throw.* imperative)
- `SharedKernel.Configuration` — AddValidatedOptions startup-validation pattern
- `SharedKernel.FeatureManagement` — IFeatureManager abstraction over Microsoft.FeatureManagement
- `SharedKernel.Cryptography` — password hashing, AES-256-GCM, RSA/ECDSA + HMAC signing, secure random (added WO-033, closed 2026-06-26)

All six phases (Design/Scaffold/Core/Tests/Docs/Published) are `●` complete across every package. No active work remains in 01.Core unless a new work order arrives.

**Why:** `SharedKernel.Guards` was added in P-003 after the original four packages were published; `SharedKernel.Cryptography` was added in WO-033 to give non-identity worker services dependency-free crypto primitives without pulling in `12.Security`'s OIDC/JWT stack.

**How to apply:** If a new phase/work-order arrives for 01.Core, it will either extend an existing package or add a seventh. Check `01.Core/state-map.md` Phase Key Registry for any new `SK.01.*` key before assuming this snapshot is current.
