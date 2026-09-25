# SharedKernel.Presentation.Core

What the HTTP and gRPC boundary packages share, with no ASP.NET Core reference:

- **Authorization attributes** (`SharedKernel.Presentation.Authorization`): `[RequireRole]`, `[RequirePermission]`, `[RequireFreshAuthentication]`, `[RequireAuthenticationMethod]`. `SharedKernel.Presentation.WebApi` evaluates them on endpoints; `SharedKernel.Presentation.Grpc` evaluates the same attributes on service methods, so a service has one authorization dialect.
- **Status maps** (`SharedKernel.Presentation.Errors`): `ErrorTypeStatusCodeMap.Resolve(ErrorType)` → HTTP status code, `GrpcStatusCodeMap.Resolve(ErrorType)` → `Grpc.Core.StatusCode`.

Services normally get this package transitively through `SharedKernel.Presentation.WebApi` or `SharedKernel.Presentation.Grpc`.
