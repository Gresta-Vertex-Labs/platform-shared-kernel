# Memory Index

- [08.Storage status snapshot](project_08storage_status.md) — domain complete end to end (all 6 phases ●) as of SK.08.Published closeout, 2026-07-18
- [Design-phase verification approach](feedback_design_phase_verification.md) — independently recount "N-member"/"N models" claims in brain docs, don't trust stated totals
- [Core vs Tests phase boundary](feedback_core_tests_phase_boundary.md) — don't write the Tests-phase xUnit suite during a Core-phase dispatch; this domain splits them
- [Split phase on missing upstream fixture](feedback_split_phase_on_missing_upstream_fixture.md) — verify fixtures on disk, implement every genuinely unblocked task, don't inherit a summary sentence's over-broad "blocked" scope
- [Re-verify blocker even same-day](feedback_reverify_blocker_even_same_day.md) — don't trust a "verified today" state-map note at face value; re-run the actual on-disk checks yourself
- [Docs-phase pattern: GenerateDocumentationFile+TreatWarningsAsErrors+NuGet metadata](feedback_docs_phase_pattern.md) — repo-wide convention; also covers the PackageReadmeFile gap only `dotnet pack` (not `build`) catches
- [Published-phase consumer-verify harness pattern](feedback_published_phase_consumer_verify.md) — standalone console project (not xUnit), IHost.StartAsync() technique for proving ValidateOnStart() failures, keyed-DI README-example verification
