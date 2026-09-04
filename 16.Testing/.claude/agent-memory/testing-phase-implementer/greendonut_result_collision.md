---
name: greendonut_result_collision
description: SharedKernel.Testing.csproj has a CS0104 ambiguity between SharedKernel.Primitives.Results.Result<T> and GreenDonut.Result<TValue> (pulled in transitively by HotChocolate.Data) — fully qualify, never alias.
type: project
---

`SharedKernel.Testing.csproj` references `HotChocolate.Data` (for `GraphQLTestExecutorFactory`),
which transitively global-imports `GreenDonut`. `GreenDonut.Result<TValue>` collides with
`SharedKernel.Primitives.Results.Result<T>` — any bare `Result<T>` or `Result` reference in a file
under `SharedKernel.Testing` (not `SharedKernel.Testing.SelfTests`, which doesn't reference
HotChocolate) produces `CS0104` at build time.

**Why:** `GreenDonut.Error`/`KeyNotFoundException` also collide with `SharedKernel.Primitives`
equivalents in some contexts.

**How to apply:** Whenever writing a new fake/type in `16.Testing/SharedKernel.Testing/` (not
SelfTests) that returns `Result<T>`/`Result`/`Error`, fully qualify inline as
`SharedKernel.Primitives.Results.Result<T>` / `SharedKernel.Primitives.Errors.Error` — a `using`
alias cannot bind an open generic like `Result<T>` so aliasing doesn't fix it. Confirmed this is
already the established codebase convention (see `FakeSymmetricEncryptionService.cs`), not
something I invented. Hit this again on `FakeEnvelopeEncryptionProvider.cs` (2026-09-01) — same
fix applied.
