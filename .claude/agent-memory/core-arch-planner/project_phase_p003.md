---
name: project-phase-p003
description: Key design decisions locked in by P-003 (SharedKernel.Guards) for 01.Core domain
metadata:
  type: project
---

P-003 introduces `SharedKernel.Guards` — a two-path guard system — as the fifth package in `01.Core`.

**Why:** Domain constructors and application-layer handlers needed a consistent precondition vocabulary. Ad-hoc null checks and bare throws produced inconsistent Error codes and unobservable failures on the railway monad. The two-path design satisfies both usage styles without code duplication.

**How to apply:** Treat the following decisions as locked for Guards.

Key locked decisions:
- `IGuardClause` is a **public marker interface** with no members. `DefaultGuardClause` is `private sealed` inside the `Guard` class — callers never reference it.
- `Guard.Against` is the single static factory returning `IGuardClause`. All guard logic is chained via extension methods on that interface.
- Guard extensions return `Error?` — **null means passed**, non-null means violated. Do NOT use `Error.None` as the "passed" sentinel in guard return values.
- `Guard.Throw` is a nested static class that mirrors every `Against.*` extension as a void method. It calls the matching extension and throws `DomainException(error)` on non-null return. No independent logic in Throw.
- `InvalidFormat` uses a `ConcurrentDictionary<string, Regex>` keyed by pattern for cached compiled regexes with a bounded timeout. `Email` uses a dedicated static compiled Regex field (not the dictionary). **Zero new Regex instances per call** in both cases.
- `Default<T>` uses `EqualityComparer<T>.Default` — no reflection, AOT-safe via generic specialization.
- `OutOfRange<T>` is constrained to `IComparable<T>` — static dispatch, no boxing for value types.
- `InvalidSmartEnum<TEnum, TValue>` constrained to `TEnum : SmartEnum<TEnum, TValue>`, calls `TryFromValue` — zero reflection.
- `GuardDescriptions` is `internal` — message templates as `const string` with `{0}`/`{1}` placeholders; `string.Format` at call site.
- Collection guards enumerate the source **at most once** per call.
- `SharedKernel.Guards` has zero NuGet dependencies — only project refs to `SharedKernel.Primitives` and `SharedKernel.Core`.
- `DomainException` (from `SharedKernel.Core`) is the exception type thrown by the imperative path.

State-map task counts after P-003:
- Design: 16 tasks (added D-13 through D-16)
- Scaffold: 15 tasks (added S-12 through S-15)
- Core: 29 tasks (added C-16 through C-29)
- Tests: 18 tasks (added T-11 through T-18)
- Docs: 6 tasks (added DO-05 through DO-06)
- Published: 9 tasks (added P-07 through P-09)
- Total: 87 tasks across 6 phases

[[project-phase-p001-p002]]
