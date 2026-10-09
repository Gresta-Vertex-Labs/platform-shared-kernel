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
| `SK.03.DomainErrorCodes` | Conform Domain error codes to the gold standard: public `DomainErrorCodes`, internal `DomainErrors`, stored-precision defect typed Unexpected (WO-087 / P-602) | ○ |

Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

### SK.03.DomainErrorCodes — Domain error codes conform to C1-C3 `○`

**Work order:** WO-087 / P-602 · **Depends on:** SK.00.ObservabilityReportOnly (P-583, for the zero-findings acceptance), SK.01.ObservabilityHelpers (P-580); coordinates with SK.01.CoreConformance (P-600) without depending on it

Domain stays logging-free and telemetry-free; only error codes change. Audit result: SharedKernel.Domain contains **no** `domain.rule.violated` literal and never reads `ErrorCodes.Domain.RuleViolated` (`BusinessRuleViolationException` carries `rule.Code`), so the rename to `domain.rule_violated` is entirely `SK.01.CoreConformance` C-02 and 03 owes only a stale-comment cleanup and a guard that it stays constant-free. The four codes 03 owns today are inline literals (`currency.code.invalid_format`, `currency.code.unknown` in `Currency`; `money.currency_mismatch` as `CurrencyMismatchRule.ErrorCode`; `money.stored_precision_exceeded` in a private rule inside `Money`) and already satisfy C1 (`{domain}.{snake_reason}`, optional middle area), so **no domain-owned wire value is renamed**. The phase adds the C2 pair: a public `SharedKernel.Domain.DomainErrorCodes` (nested groups `Currency` and `Money`, mirroring Primitives' `ErrorCodes` style; `not_found.default` and `validation.required` stay `ErrorCodes` from Primitives, never re-declared) and an internal `Internal.DomainErrors` factory, and fixes C3: a persisted amount with more decimals than its currency allows is corrupt stored data (a defect, not a caller's rule violation), so it becomes `ErrorType.Unexpected` (HTTP 500) instead of `BusinessRule` (422). `CurrencyMismatchRule` stays `BusinessRule` (a rule the caller broke).

| ID | Task | Package | State |
| --- | --- | --- | :---: |
| D-01 | Ratify the inventory: four owned codes keep their wire values (all C1-clean); `DomainErrorCodes` lives in namespace `SharedKernel.Domain` with groups `Currency` (`InvalidFormat`, `Unknown`) and `Money` (`CurrencyMismatch`, `StoredPrecisionExceeded`); `CurrencyMismatchRule.ErrorCode` stays as a const aliasing `DomainErrorCodes.Money.CurrencyMismatch` (value unchanged, no PublicAPI text change for it) | SharedKernel.Domain | `○` |
| D-02 | Ratify the C3 change: `money.stored_precision_exceeded` is thrown as a `DomainException` carrying `Error.Unexpected(...)` from the internal factory (not through `CheckRule`), so the `StoredPrecisionRule` class is deleted; recorded as a breaking behaviour change (exception type `BusinessRuleViolationException` to `DomainException`, ErrorType `BusinessRule` to `Unexpected`). Confirm no other code in the repo catches the old type (audit found none outside Domain) | SharedKernel.Domain | `○` |
| D-03 | Ratify the E1 exemption: `BusinessRuleViolationException(IBusinessRule)` and `DomainNotFoundException(Type, object)` derive `SharedKernelException` through `DomainException` and deliberately do **not** gain `(Error)`/`(Error, Exception)` constructors (an arbitrary `Error` would bypass `IBusinessRule.Code`); recorded under Decisions and reported to arch-lead so P-583's check and E1's wording accept it | SharedKernel.Domain | `○` |
| C-01 | Add `DomainErrorCodes` (public static, XML-documented, constants only) and internal `DomainErrors` with factories `CurrencyInvalidFormat()`, `CurrencyUnknown(string code)`, `StoredPrecisionExceeded(decimal amount, Currency currency)`; messages one sentence ending in a period (E3) | SharedKernel.Domain | `○` |
| C-02 | `Currency.Validate` uses the factory (same codes, messages unchanged); `CurrencyMismatchRule.ErrorCode`/`Code` use the constant | SharedKernel.Domain | `○` |
| C-03 | `Money` materialization constructor: replace `CheckRule(new StoredPrecisionRule(...))` with a direct `DomainException(DomainErrors.StoredPrecisionExceeded(...))` guarded by the same precision test; delete `StoredPrecisionRule`; update the XML docs (`BusinessRuleViolationException` mentions, `<exception>` tags) | SharedKernel.Domain | `○` |
| C-04 | Remove the stale `domain.rule.violated` mention from the Domain test comment and the Money/MoneyExtensions XML docs that spell codes as literals (`<c>money.currency_mismatch</c>` becomes a `cref` to the constant where the doc references the code) | SharedKernel.Domain | `○` |
| C-05 | `PublicAPI.Unshipped.txt`: add `DomainErrorCodes` and its nested constants; keep `CurrencyMismatchRule.ErrorCode`; no `*REMOVED*` line is needed (the package has not shipped) | SharedKernel.Domain | `○` |
| T-01 | New `DomainErrorCodesTests`: pin every constant by literal; assert each matches the C1 pattern (lowercase `segment.segment[.segment]`, snake reason); reflection check that every `const string` in `DomainErrorCodes` is covered so a new code cannot skip the pin | SharedKernel.Domain.Tests | `○` |
| T-02 | Error-type tests through the real paths: `Currency.Create("")`/`Create("ZZZ")` yield `Validation` with the constants; mismatched `Money` addition yields `BusinessRuleViolationException` with `BusinessRule` and the constant; the stored-precision path yields `DomainException` with `Unexpected` and the constant (add to `MoneyHardeningTests`; fails when D-02 is reverted) | SharedKernel.Domain.Tests | `○` |
| T-03 | Guard: the Domain assembly never references `ErrorCodes.Domain.RuleViolated` (reflection over IL string/field refs or a source-text scan in the test project), so the Primitives rename cannot silently need a Domain change | SharedKernel.Domain.Tests | `○` |
| T-04 | Make test fixtures C1-conform where P-583's scan could see them (`SingleValueObjectTests`: `Email.Required` to `email.required`, `Email.InvalidFormat` to `email.invalid_format`, `Int.NotPositive` to `int.not_positive`); assertions follow | SharedKernel.Domain.Tests | `○` |
| T-05 | Run the P-583 report-only error-code assertion (per-domain count) and record zero findings for the Domain assemblies in the PR body | SharedKernel.Domain.Tests | `○` |
| DO-01 | README "Error codes" table: reference `DomainErrorCodes` constants, add an `ErrorType` column (the stored-precision row becomes Unexpected), and a compiled snippet branching on `DomainErrorCodes.Money.CurrencyMismatch` with its real output | SharedKernel.Domain | `○` |
| DO-02 | `SharedKernel.Domain.ConsumerVerify`: assert the constants from the packed package and a currency-mismatch catch by code | SharedKernel.Domain.ConsumerVerify | `○` |
| DO-03 | Domain `CLAUDE.md`: rule 31/34 mention the constants, new rule for `DomainErrorCodes`/`DomainErrors`, Logging section states telemetry-free, D-03 decision; PR body carries a "Breaking changes" heading (stored-precision type and ErrorType change; `DomainErrorCodes` added; no wire rename of Domain-owned codes) | SharedKernel.Domain | `○` |

**Acceptance:**
- [ ] Codes follow C1-C3; the P-583 report-only tests show zero findings for the domain.
- [ ] Breaking renames are listed for the release notes: no Domain-owned wire value changes; the stored-precision exception type and ErrorType change is listed; `PublicAPI.Unshipped.txt`, the package README (error-code table with ErrorType) and the domain `CLAUDE.md` are updated in the same change.
- [ ] Tests assert error codes and ErrorTypes through the P-581 capture helpers where telemetry or logs are involved (none expected: the domain emits neither), and by constant plus literal pin otherwise; no string search of source.
- [ ] No `domain.rule.violated` literal or `ErrorCodes.Domain.RuleViolated` read exists in SharedKernel.Domain; the domain still references no logging, `ActivitySource` or `Meter`.

## Blocked

None.

## Cross-Domain Dependencies

- **01.Core (`SK.01.CoreConformance` C-02):** owns the `ErrorCodes.Domain.RuleViolated` value change to `domain.rule_violated`. Domain holds no literal and does not read the constant, so this phase needs nothing from it; the only trace of the old code in 03 is a test comment, removed by C-04.
- **06.Persistence (note only):** a stored `Money` amount with excess decimals now surfaces as `DomainException` with `ErrorType.Unexpected` (500) instead of `BusinessRuleViolationException` (422); check any persistence test or handler that expects the old type (`MoneyMapping`, `CurrencyValueConverter`).
- **00.Governance (`SK.00.ObservabilityReportOnly`):** the error-code format assertion must accept the exemption in D-03 and read `DomainErrorCodes` constants.
- **16.Testing / samples (note only):** `MoneyFaker` and `Shop.Ordering.Domain` use no changed code; verify nothing spells `money.currency_mismatch` by literal.
