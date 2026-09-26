---
name: project_grpc_webapi_reference_decision
description: What WebApi and Grpc share lives in SharedKernel.Presentation.Core (WO-086) — Grpc no longer references WebApi; GrpcStatusCodeMap stays a sibling of ErrorTypeStatusCodeMap
metadata:
  type: project
---

> WO-086 (2026-09): the `.Grpc` → `.WebApi` reference described in the original WO-074/P-468 decision is gone. The four `Require*` attributes (namespace `SharedKernel.Presentation.Authorization`) and both status maps (namespace `SharedKernel.Presentation.Errors`) moved into the new Host-tier `SharedKernel.Presentation.Core` (references `SharedKernel.Primitives` + `Grpc.Core.Api` only), which WebApi and Grpc both reference.

**Current rule:** when two `14.Presentation` packages need the same type, it goes into `SharedKernel.Presentation.Core` — never a sibling-to-sibling reference and never a duplicate. This resolved the old tension with `SharedKernel.Presentation.SignalR`'s declined `.WebApi` reference (P-418/D-65, declined because it would drag `Asp.Versioning`/OpenAPI/Scalar into a real-time service): with a small shared package, neither side pulls in a whole sibling's transitive surface. Before adding anything to `.Core`, keep it small — every WebApi and Grpc consumer pays for it.

**Still valid from WO-074:** `GrpcStatusCodeMap` (`ErrorType → StatusCode`) is a sibling to `ErrorTypeStatusCodeMap`, never merged — HTTP and gRPC status codes don't correspond 1:1, so the shared vocabulary is `SharedKernel.Primitives`' `ErrorType` and each protocol gets its own table. A future inbound protocol gets its own `{Protocol}StatusCodeMap`. `SharedKernel.Presentation.Grpc` still never references `SharedKernel.Contracts` (architecture test).
