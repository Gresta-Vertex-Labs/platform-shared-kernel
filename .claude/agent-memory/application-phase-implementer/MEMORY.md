# Memory Index

- [Generic Result/Result<T> failure construction](generic_result_failure_construction.md) — Expression-compiled FailureResponseFactory (not dynamic, not reflection) for AuthorizationBehavior/IdempotentCommandBehavior
- [Seven-step pipeline implementation notes](seven_step_pipeline_implementation.md) — file locations, csproj additions, phase-scope clarification for 05.Application Core phase
- [Docs phase XML doc enforcement pattern](docs_phase_xml_doc_enforcement.md) — GenerateDocumentationFile/TreatWarningsAsErrors as a build gate; cref-qualification and `<inheritdoc/>` fixes found
- [WO-038 Tests phase completion — key fixes and pitfalls](project_wo038_tests_complete.md) — AggregateException/WhenAll, LoggerMessage+NSubstitute, static Meter pollution, ChannelFireAndForgetDispatcher internal, guard behavior intercept, ValidationException ambiguity
- [WO-039 P-241 partial unblock](project_wo039_p241_partial_unblock.md) — only 1 of 4 real-assembly rule groups actually depends on the 00.Governance P-240 blocker; verify sub-parts, don't inherit blanket "still blocked" notes
- [WO-041 logging retrofit](project_wo041_logging_retrofit.md) — stale "Design locked" prose vs real task table; [LoggerMessage]+NSubstitute IsEnabled pitfall; testing internal types' ILogger<T>; Mono.Cecil generated-code exclusion; ChannelFireAndForgetDispatcher DropOldest quirk
- [WO-058 Design verification pass](project_wo058_design_verification.md) — same-day arch-planner dispatches can already be fully written; don't close a multi-phase Root Backlog ID early; root state-map.md's live tail is past line 13k, not at the `## Changelog` heading
