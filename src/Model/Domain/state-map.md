# 03.Domain — State Map

> Living board for this domain: what exists and what is open. Completed work is not kept here; `git log` is the record.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## Package Board

| Package | Tier | Status | Notes |
| --- | --- | --- | --- |
| `SharedKernel.Domain` | Model | ● | `Entity`, `AggregateRoot` with audit/soft-delete/tenanted bases (`IHasTenant.TenantId` is a `TenantId`), `ValueObject`, `StronglyTypedId`, `DomainEvent`, `IBusinessRule`, `IPolicy`, specifications with offset/keyset paging, `Money`. References `SharedKernel.Execution`; logging-free; never references `SharedKernel.Contracts`. Ships with the repo-wide release train. |

## Phase Key Registry

| Phase key | Phase | Status |
| --- | --- | --- |
| `SK.03.RuleResult` | Non-throwing business-rule check (`TryCheckRule`) | ○ |

Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

### SK.03.RuleResult — Non-throwing business-rule check `○`

**Work order:** WO-090 / P-627 · **Depends on:** None

Add `TryCheckRule(IBusinessRule)` next to `CheckRule` on `AggregateRoot<TId>`, `ValueObject` and `DomainService`: a protected static member returning `Result`, `Result.Success()` when the rule holds and `Result.Failure(error)` when broken, where `error` is exactly the `Error` a `BusinessRuleViolationException` for that rule would carry (`ErrorType.BusinessRule`, the rule's own `Code` and `Message`). Behavior methods that return `Result` stop wrapping `CheckRule` in `ResultTry` or hand-mapping the rule to an error. Purely additive: `CheckRule`, `TryCreate` and every existing behavior are unchanged, and the new members are the only public API delta for a 1.0.0 consumer.

| ID | Task | Package | State |
| --- | --- | --- | :---: |
| D-01 | Ratify the shape: name `TryCheckRule`; `protected static Result` on the three bases (same visibility and staticness as `CheckRule`, so every call site that can call `CheckRule` can call it); null rule throws `ArgumentNullException` exactly like `CheckRule` (a programming error, not a failed result); a rule is evaluated once and `Code`/`Message` read once, as the exception does. Walk the "If you change…" table: `IBusinessRule` is unchanged (no new interface member, so no consumer rule breaks); no 06.Persistence coupling | SharedKernel.Domain | `○` |
| D-02 | Ratify error parity by construction, not by duplication: one internal builder produces the rule's `Error` (`Error.BusinessRule(rule.Code, rule.Message)`); `BusinessRuleViolationException` and the new check both call it, so the two paths cannot drift. Composite rules (`And`/`Or`/`Not`) need no special case because they report through `Code`/`Message` (rule 12) | SharedKernel.Domain | `○` |
| C-01 | Add internal `DomainInvariants.TryCheckRule(IBusinessRule)` (the only implementation, rule 13) and the shared internal error builder; point `BusinessRuleViolationException`'s `CreateError` at the builder with no behavior change | SharedKernel.Domain | `○` |
| C-02 | Add `protected static Result TryCheckRule(IBusinessRule rule)` with XML docs to `AggregateRoot<TId>`, `ValueObject` and `DomainService`, each delegating to `DomainInvariants`; docs cross-reference `CheckRule` and state the rule of thumb (constructors and factories throw, `Result`-returning behavior methods use `TryCheckRule`) | SharedKernel.Domain | `○` |
| C-03 | Update the `IBusinessRule`, `BusinessRuleViolationException` and `IPolicy` XML remarks that name `CheckRule` as the only enforcement path; no work-order ids in docs | SharedKernel.Domain | `○` |
| C-04 | Record the three new members in `PublicAPI.Unshipped.txt` (no `Shipped` line changes) | SharedKernel.Domain | `○` |
| T-01 | `TryCheckRule` on each of the three bases: satisfied rule returns success; broken rule returns failure with `ErrorType.BusinessRule`, the rule's code and message; null rule throws `ArgumentNullException` | SharedKernel.Domain.Tests | `○` |
| T-02 | Parity: for the same broken rule (simple, `And`, `Or`, `Not`, and a `policy.ToRule(...)` rule) the failure `Error` equals `BusinessRuleViolationException.Error` from `CheckRule` (code, message, type); a case in `DomainHardeningTests` that fails if the exception and the result builders diverge | SharedKernel.Domain.Tests | `○` |
| T-03 | Behavior-method sample: an aggregate method returning `Result` via `TryCheckRule` produces the same failure the `ResultTry`-wrapped `CheckRule` does, and no exception is thrown on the broken path | SharedKernel.Domain.Tests | `○` |
| T-04 | Extend `SharedKernel.Domain.ConsumerVerify` to call `TryCheckRule` from an aggregate, a value object and a domain service against the packed package | SharedKernel.Domain.ConsumerVerify | `○` |
| DO-01 | README "Business rules" section: document `TryCheckRule` next to `CheckRule` with the rule of thumb and a compiled snippet showing real output (error code, message, `BusinessRule` type); extend the "available on" sentence; keep any `ReadmeSample*Tests` in step | SharedKernel.Domain | `○` |

**Acceptance:**
- [ ] `TryCheckRule` exists on `AggregateRoot<TId>`, `ValueObject` and `DomainService`; it returns success when the rule holds and a failure whose `Error` equals the throwing path's exception `Error` (code, message, `ErrorType.BusinessRule`) when broken.
- [ ] The README documents it beside `CheckRule` with the constructors-throw / behavior-methods-return-`Result` rule of thumb; the snippet compiles and its shown outputs come from running it.
- [ ] Unit tests cover broken, satisfied, composite and policy-bridged rules and error parity with the throwing path; `ConsumerVerify` exercises it on the packed package.
- [ ] No 1.0.0 public member changes: `PublicAPI.Shipped.txt` untouched, the three new members listed in `PublicAPI.Unshipped.txt`; `CheckRule`, `TryCreate` and `IBusinessRule` behave as before.
- [ ] No testing fake or consumer-skill change is needed: neither references `CheckRule` today (verified by search); revisit only if that changes.

## Blocked

None.

## Cross-Domain Dependencies

None open.
