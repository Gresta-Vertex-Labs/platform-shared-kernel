# Communication Phase Implementer — Memory Index

- [Project: SharedKernel Communication](project_communication.md) — 11.Communication domain status, package versions, key implementation patterns
- [Feedback: caller from IRequestContextAccessor](feedback_request_context_accessor.md) — outbound propagation reads IRequestContextAccessor, never IUserContext/IHttpContextAccessor
- [Feedback: gRPC namespace patterns](feedback_grpc_namespaces.md) — namespace alias, InterceptorScope location, ServiceConfig namespace
- [Project: HC v16 GraphQL patterns](project_hc_v16_patterns.md) — HotChocolate v16 API specifics (historical: GraphQL moved to 14.Presentation in WO-086)
- [Feedback: logging retrofit test patterns](feedback_logging_retrofit_test_patterns.md) — EventId/Level regression via reflection, real-assembly LoggingEventIdIntegrityAssertion, temp-analyzer SK0020/21 verify-then-revert
- [Feedback: Grpc.Core.Metadata lowercase casing](feedback_grpc_metadata_casing.md) — Metadata normalizes keys to lowercase internally; affects test assertions reading back stored keys
- [Feedback: gRPC deadline test technique + TTL-cache reflection seeding](feedback_grpc_deadline_test_technique.md) — deadline-exceeded behavioral proof recipe; ServiceEndpointResolver never re-queries a resolved name, seed via reflection instead
