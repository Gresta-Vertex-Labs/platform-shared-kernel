---
name: project-phase-p001-p002
description: Key design decisions locked in by P-001 (Core Primitives) and P-002 (Railway Extensions + BCL) for 01.Core domain
metadata:
  type: project
---

P-001 and P-002 are the foundational phases for SharedKernel.Primitives and SharedKernel.Core.

**Why:** These two phases establish the type shapes that every other domain and downstream microservice depends on. Breaking changes after publication require coordinated ecosystem-wide upgrades.

**How to apply:** Treat the following decisions as locked — do not revisit without a versioning plan.

Key locked decisions:
- `Result<T>` is a **sealed class** (not struct) — generic struct zero-value problem ruled out structs
- `Result` (non-generic) is a **readonly struct** — no typed payload, zero-value safe
- `ValidationResult` / `ValidationResult<T>` are distinct sealed records from `Result<T>` — multi-error aggregate; never conflate with single-error monad
- `ErrorCodes` uses nested static class string constants, not enums — extensibility without enum versioning
- `Error.None` sentinel; null is never used for absent-error
- `SmartEnum` lookup is static list at type-init, no reflection in hot path
- Async railway extensions must not use async/await on outer extension body (avoid state machine allocation)
- `IClock` only — `DateTime.UtcNow` / `DateTimeOffset.UtcNow` direct use is a hard violation

State-map task counts after P-001 + P-002:
- Design: 12 tasks (added D-11 ValidationResult design, D-12 ErrorCodes design)
- Core: 15 tasks (added C-14 ValidationResult impl, C-15 ErrorCodes impl)
- Tests: 10 tasks (added T-10 ValidationResult tests)
- Total: 54 tasks across 6 phases
