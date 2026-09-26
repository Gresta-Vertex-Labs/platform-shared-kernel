---
name: feedback-grpc-namespaces
description: gRPC namespace pitfalls — alias required for Grpc.Core, InterceptorScope location, ServiceConfig location
metadata:
  type: feedback
---

Three namespace-related traps in the Grpc package implementation:

**1. Namespace alias required for Grpc.Core**
The project namespace `SharedKernel.Communication.Grpc` collides with the NuGet namespace `Grpc.Core`. Add `using GrpcCore = Grpc.Core;` in any file that needs to reference both (e.g., `Builders/GrpcCommunicationBuilder.cs`).

**Why:** Without the alias, `Grpc.Core.Channel` becomes ambiguous. The alias eliminates ambiguity cleanly.

**2. InterceptorScope lives in Grpc.Net.ClientFactory**
`InterceptorScope.Channel` is in `Grpc.Net.ClientFactory` namespace, not `Grpc.Core`. Add `using Grpc.Net.ClientFactory;` in the builder file.

**How to apply:** When registering interceptors: `.AddInterceptor<MyInterceptor>(InterceptorScope.Channel)` requires `using Grpc.Net.ClientFactory;`.

**3. Retry policy types live in Grpc.Net.Client.Configuration**
`ServiceConfig`, `MethodConfig`, `RetryPolicy`, and `MethodName` are in `Grpc.Net.Client.Configuration`, not the root `Grpc.Net.Client`. Add `using Grpc.Net.Client.Configuration;` in the builder.
