# Memory Index

- [Project: SharedKernel Core Domain](project_core_domain.md) — 01.Core status; WO-049 gold-standard review in progress, P-292/P-293 implemented, P-294–P-298 pending
- [Feedback: XML docs already on Guards from impl phase](feedback_guards_xml_docs_already_present.md) — Guards XML docs were complete in the Core phase; DO-05 was effectively done before Docs phase started
- [Reference: local NuGet feed convention](reference_local_nuget_feed.md) — ./nupkgs/ + root NuGet.Config is the real "publish to feed"; GenerateDocumentationFile only added at Published phase
- [Feedback: verify design claims before shipping](feedback_verify_design_claims_before_shipping.md) — a locked design's prose can be factually wrong (BCL behavior, codebase state); verify empirically, correct via sync-brain
- [Feedback: sync-brain "domain:" field routes sub-domain only](feedback_syncbrain_domain_field_routes_to_subdomain_only.md) — a "domain: 01.Core" call never reaches root CLAUDE.md; issue a second call with no domain: field for root edits
