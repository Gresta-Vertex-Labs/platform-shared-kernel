# Memory Index

- [16.Testing domain conventions](domain_16_testing_conventions.md) — folder isolation, SelfTests routing rule, blocked-vs-pending distinction, Docs-phase-is-usually-a-diff pattern, Phase Backlog auto-close precedent
- [Cross-domain source verification discipline](feedback_verify_live_source.md) — always re-read the owning domain's actual .cs files, never trust CLAUDE.md prose or even a prior session's own draft
- [Check-already-done before working](feedback_check_already_done_before_working.md) — a phase brief's "○ Pending" claim can be stale; read state-map.md's actual task states first, especially on same-day rapid dispatch-then-implement cycles
- [WO-043 P-268/P-269 status](project_wo043_storage_testing.md) — CLOSED 2026-07-18, all six 16.Testing phases ● again, root Phase Backlog P-268/P-269 also flipped
- [WO-044 P-275/P-276 status](project_wo044_search_testing.md) — Design/Scaffold/Core all CLOSED 2026-07-19/20 (Core: MeilisearchContainerFixture/ElasticsearchContainerFixture + Search/ fakes); Tests/Docs still pending
- [Testcontainers 4.13.0 ctor break](feedback_testcontainers_413_ctor_break.md) — a "just bump the pin" task can be a real breaking change (obsolete parameterless builder ctors); always rebuild, read warnings, verify with real Docker if available
- [GreenDonut Result<T> ambiguity](feedback_greendonut_result_ambiguity.md) — HotChocolate.Data's transitive GreenDonut.Result<T> collides with SharedKernel's Result<T> in SharedKernel.Testing.csproj only; SelfTests unaffected; bare Error is ALSO ambiguous (vs HotChocolate.Error)
- [Build tooling gotchas](feedback_build_tooling_gotchas.md) — PowerShell 5.1 mangles UTF-8 em-dashes on bulk regex edits (use perl or the Edit tool instead); TreatWarningsAsErrors is not actually wired into any csproj in this repo despite root CLAUDE.md's framing
