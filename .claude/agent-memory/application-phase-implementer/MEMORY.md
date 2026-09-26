# Memory Index

- [Generic Result/Result<T> failure construction](generic_result_failure_construction.md) — FailureResponse.Create<TResponse> (cached Failure(Error) delegate) in SharedKernel.Application.Pipeline; reuse it, never `dynamic`
- [Docs phase XML doc enforcement pattern](docs_phase_xml_doc_enforcement.md) — GenerateDocumentationFile/TreatWarningsAsErrors as a build gate; cref-qualification and `<inheritdoc/>` fixes found
- [WO-038 Tests phase completion — key fixes and pitfalls](project_wo038_tests_complete.md) — LoggerMessage+NSubstitute IsEnabled, static Meter pollution, [EnumeratorCancellation]; fire-and-forget/parallel-dispatch parts historical
- [WO-039 P-241 partial unblock](project_wo039_p241_partial_unblock.md) — only 1 of 4 real-assembly rule groups actually depends on the 00.Governance P-240 blocker; verify sub-parts, don't inherit blanket "still blocked" notes
- [WO-041 logging retrofit](project_wo041_logging_retrofit.md) — stale "Design locked" prose vs real task table; [LoggerMessage]+NSubstitute IsEnabled pitfall; testing internal types' ILogger<T>; Mono.Cecil generated-code exclusion
- [WO-058 Design verification pass](project_wo058_design_verification.md) — same-day arch-planner dispatches can already be fully written; don't close a multi-phase Root Backlog ID early; find the root state-map.md's live tail
- [Published-phase batch closure 2026-08-17](project_wo_published_batch_2026_08_17.md) — consumer-verify is the substantive deliverable; NSubstitute/Castle can't proxy ILogger<T> over a private-nested T; verify cross-domain fakes were actually built, not just dispatched
