---
name: project-phase-p042
description: Locked design for P-042 — ErrorType.BusinessRule, Error.BusinessRule factory, ErrorCodes.Domain.RuleViolated in SharedKernel.Primitives
metadata:
  type: project
---

P-042 adds `ErrorType.BusinessRule` to the `ErrorType` enum and a matching `Error.BusinessRule(string code, string message)` factory to the `Error` sealed record in `SharedKernel.Primitives`. Also adds `ErrorCodes.Domain` nested static class with `RuleViolated = "domain.rule.violated"` constant.

**Why:** `BusinessRuleViolationException` in `03.Domain` was using `Error.Unexpected(...)` to represent domain invariant violations — a semantic misclassification. `Unexpected` signals a system fault (maps to HTTP 500); a domain rule violation is an expected, predictable rejection that should map to HTTP 422. Downstream effects of the misclassification: wrong HTTP status, false-positive alert noise, loss of caller error discrimination.

**How to apply:**
- `ErrorType.BusinessRule` is categorically: expected, domain-driven, HTTP 422 — distinct from `Validation` (input format/presence) and `Unexpected` (system fault).
- Presentation-layer middleware must map `ErrorType.BusinessRule` → HTTP 422, same as `Validation`, but keep them as distinct enum values for audit/monitoring differentiation.
- `ErrorCodes.Domain.RuleViolated` is the canonical code for `BusinessRuleViolationException` in `03.Domain` — do not use a raw string literal in the domain layer.
- Change is purely additive — all existing `ErrorType` values are unchanged; no existing tests break.

Relates to [[project-phase-p001-p002]] (original ErrorType and Error factory design).
