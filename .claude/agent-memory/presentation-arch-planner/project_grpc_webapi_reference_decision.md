---
name: project_grpc_webapi_reference_decision
description: SharedKernel.Presentation.Grpc deliberately ProjectReferences .WebApi for Authorization/ attribute reuse — the one exception to this domain's sibling-independence rule
metadata:
  type: project
---

`SharedKernel.Presentation.Grpc` (WO-074, P-468, design-locked 2026-08-26, Core not yet implemented) takes a deliberate `ProjectReference` on `SharedKernel.Presentation.WebApi` — the one case where two `14.Presentation` sibling packages reference each other. It exists solely to reuse `RequireRoleAttribute`/`RequirePermissionAttribute`/`RequireFreshAuthenticationAttribute`/`RequireAuthenticationMethodAttribute` verbatim, so the platform has one declarative-authorization dialect across HTTP and gRPC instead of two independently-maintained attribute sets.

**Why this does NOT contradict `SharedKernel.Presentation.SignalR`'s declined identical-shaped reference (P-418/D-65):** SignalR declined a `.WebApi` reference to avoid pulling `Asp.Versioning`/`Microsoft.AspNetCore.OpenApi`/`Scalar.AspNetCore` transitively into a pure real-time service that may not want HTTP at all — that reference would have been broad (an entire package's transitive surface) for a narrow gain (one options type, `CorsPolicyOptions`). `.Grpc`'s reference is narrow (four small `System.Attribute` types, zero further transitive NuGet surface) for a real gain the acceptance criteria explicitly demanded (no duplicated attribute vocabulary).

**How to apply:** When planning a future `14.Presentation` capability that might want to reuse a type from a sibling package, don't reason by analogy to either decision alone — re-derive the cost/benefit each time: what's the actual transitive NuGet/package surface being pulled in, and is duplication genuinely worse than the coupling? Both decisions are recorded in `14.Presentation/CLAUDE.md` under "Why `.Grpc` references `.WebApi`" (right after "Why SignalR's Redis backplane is distinct from `02.Caching.Redis.PubSub`") — read that section before proposing a third cross-sibling reference in either direction.

**Also established in this same phase:** `GrpcStatusCodeMap` (new, `ErrorType→StatusCode`) is a sibling to `ErrorTypeStatusCodeMap`, never merged — same pattern as any future protocol-specific status map: HTTP/gRPC/anything-else status enums don't correspond 1:1, so the shared vocabulary point is `01.Core`'s `ErrorType`, and each protocol gets its own mapping table keyed off it. If a fourth inbound protocol ever needs this domain's attention, expect the same pattern (a new `{Protocol}StatusCodeMap`, never a generalized cross-protocol table).
