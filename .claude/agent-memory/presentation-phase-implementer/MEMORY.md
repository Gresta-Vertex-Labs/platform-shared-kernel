# Memory Index

- [Verify real API shapes via reflection](feedback_verify_real_api_shapes.md) — design docs/training data drift from actual installed NuGet package type names; probe before coding against Asp.Versioning/Scalar/OpenAPI/SignalR/Grpc.AspNetCore types
- [GenerateDocumentationFile is now centralized](feedback_docs_phase_generatedocfile.md) — no longer a per-project Docs-phase gotcha; set repo-wide in Directory.Build.targets, active from a package's first build
- [Gated multi-phase WO closeout pattern](feedback_gated_wo_closeout_pattern.md) — single root state-map-phase touch (not one per phase key), skip sync-brain for pure design-execution sessions; EXCEPTION for concurrent multi-domain dispatch — honor an explicit "don't touch root files" override
- [Testing IEndpointFilter without a host](feedback_endpoint_filter_testing.md) — EndpointFilterInvocationContext.Create/SetEndpoint/ProblemHttpResult shapes, empty-DI and throwing-property test tricks
- [In-process gRPC testing pattern](project_grpc_inprocess_testing_pattern.md) — WebApplicationFactory + hand-written .proto + ResponseVersionHandler, no sockets/Docker; reused verbatim from 13.ServiceDefaults.Tests
