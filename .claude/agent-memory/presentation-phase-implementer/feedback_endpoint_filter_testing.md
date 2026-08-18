---
name: feedback_endpoint_filter_testing
description: How to unit-test an IEndpointFilter (e.g. AuthorizationRequirementEndpointFilter) without a WebApplicationFactory host — the concrete API shapes for EndpointFilterInvocationContext/Endpoint/Results.Problem, verified via reflection probe.
type: feedback
---

When a phase needs unit tests for an `IEndpointFilter` implementation, a full `WebApplicationFactory`
host is unnecessary — build the invocation context directly. Verified via a throwaway reflection probe
against the installed net10.0 SDK (2026-08-17, SK.14.Tests/T-15–T-18):

- `EndpointFilterInvocationContext` has **no public constructor**. Use the static factory
  `EndpointFilterInvocationContext.Create(HttpContext)` (overloads up to 8 typed arguments exist but
  are rarely needed for a filter test).
- Attach endpoint metadata (attribute instances the filter reads via `GetEndpoint()?.Metadata`) with
  `httpContext.SetEndpoint(new Endpoint(requestDelegate, new EndpointMetadataCollection(metadata), displayName))`.
  The `RequestDelegate` parameter accepts `null` (or a no-op `_ => Task.CompletedTask`) at runtime
  despite its non-nullable annotation — a filter test never invokes it.
- `Results.Problem(ProblemDetails)` returns the concrete
  `Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult` — assert on its `.StatusCode`/
  `.ProblemDetails` properties directly (`result.Should().BeOfType<ProblemHttpResult>()`) rather than
  the loosely-typed `IResult`/`object?` the filter's own signature returns.
- To prove a no-op code path resolves **zero** services (not just "the test happened to pass"), leave
  `HttpContext.RequestServices` backed by an **empty** `ServiceCollection`. `GetRequiredService<T>()`
  throws `InvalidOperationException` against an empty container, so an accidental resolution attempt
  fails the test loudly instead of silently succeeding.
- To prove a rejection path never reads a specific property (e.g. `IUserContext.IsAuthenticated`, when
  the real filter is documented to reject purely via `HasRole`/`HasPermission` returning `false`), build
  a test-only implementation whose getter for that property `throw`s. A test using it fails with the
  thrown exception — not a clean rejection — the instant any code path touches that member, making a
  future regression (someone adding a bespoke `IsAuthenticated` branch) structurally impossible to miss.

**Why:** These are the same class of shape-verification surprises this domain has hit repeatedly
(`ISignalRBuilder`→`ISignalRServerBuilder`, `RouteGroupBuilder`'s real namespace, `HttpContext.RequestedApiVersion`
being a property not a method). All four facts above were confirmed via a `dotnet run` throwaway probe
project before writing any production test code, per this domain's standing "verify real API shapes"
discipline — not assumed from memory or training data.

**How to apply:** Whenever a future phase in `14.Presentation` (or any domain testing an
`IEndpointFilter`/minimal-API filter pipeline) needs unit coverage without a full host, reach for this
pattern first. See `14.Presentation/SharedKernel.Presentation.WebApi/SharedKernel.Presentation.WebApi.Tests/Authorization/EndpointFilterTestHelpers.cs`
and `IsAuthenticatedGuardUserContext.cs` for the shipped reference implementation.
