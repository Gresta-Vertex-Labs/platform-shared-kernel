# Memory Index

- [Project: SharedKernel Core Domain](project_core_domain.md) — 01.Core status; thirteen packages Published; how to recover a crashed predecessor session's uncommitted work
- [Feedback: XML docs already on Guards from impl phase](feedback_guards_xml_docs_already_present.md) — Guards XML docs were complete in the Core phase; DO-05 was effectively done before Docs phase started
- [Reference: local NuGet feed convention](reference_local_nuget_feed.md) — ./nupkgs/ + root NuGet.Config is the real "publish to feed"; GenerateDocumentationFile only added at Published phase
- [Feedback: verify design claims before shipping](feedback_verify_design_claims_before_shipping.md) — a locked design's prose can be factually wrong (BCL behavior, codebase state); verify empirically, correct via sync-brain
- [Feedback: sync-brain "domain:" field routes sub-domain only](feedback_syncbrain_domain_field_routes_to_subdomain_only.md) — a "domain: 01.Core" call never reaches root CLAUDE.md; issue a second call with no domain: field for root edits
- [Feedback: shared-file protocol + self-authored phase](feedback_shared_file_protocol_and_self_authored_phase.md) — honor an explicit root-file no-touch list over the default workflow; grep exact task-ID rows, not prose, before numbering a new phase
- [Feedback: verify positional-arg/optional-param compat claims](feedback_verify_positional_arg_compat_claims.md) — two failure modes: a positional trailing-arg call site, AND a method-group conversion whose arity changed (whole-build break, invisible to call-site grep)
