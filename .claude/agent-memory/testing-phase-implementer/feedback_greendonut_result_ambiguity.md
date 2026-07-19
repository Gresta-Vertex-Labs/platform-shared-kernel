---
name: feedback_greendonut_result_ambiguity
description: SharedKernel.Testing.csproj's HotChocolate.Data reference transitively pulls in GreenDonut.Result<TValue>, colliding with SharedKernel.Primitives.Results.Result<T> for any fake that returns Result<T> unqualified.
type: project
---

`SharedKernel.Testing.csproj` carries `HotChocolate.AspNetCore`/`HotChocolate.Data` (for `Communication/GraphQLTestExecutorFactory`, C-39). That package brings a `global using GreenDonut;` into the project's generated `GlobalUsings.g.cs`. `GreenDonut` declares its own `Result<TValue>` type (used for DataLoader batching).

**Symptom:** any new file in `SharedKernel.Testing` that writes `Result<Foo>` unqualified (even with `using SharedKernel.Primitives.Results;` present) fails with CS0104 ambiguous reference between `GreenDonut.Result<TValue>` and `SharedKernel.Primitives.Results.Result<T>`. Non-generic `Result` (no type argument) is NOT ambiguous — only the generic form collides.

This had never surfaced before P-269/WO-043's `Storage/InMemoryFileStorage.cs`/`InMemoryBlobUriGenerator.cs` because no prior fake in this package returned a generic `Result<T>` from `SharedKernel.Primitives` (the caching/messaging/security fakes return concrete types, `Task`, or non-generic `Result`).

**How to apply:** any future `16.Testing` fake whose owning interface returns `Result<T>` (`SharedKernel.Primitives.Results.Result<T>`) must fully qualify it as `SharedKernel.Primitives.Results.Result<T>` at every use site in that file — a `using` alias does not work here because C# does not support aliasing an open generic type (`using Result<T> = ...` is not legal syntax). This is a project-wide latent landmine, not specific to Storage — check for it whenever a new folder's contract involves `Result<T>`.

**Confirmed scoped to `SharedKernel.Testing.csproj` only (verified 2026-07-18 while writing T-47's SelfTests):** `SharedKernel.Testing.SelfTests.csproj` does NOT need this workaround. A project's generated `GlobalUsings.g.cs` (driven by its own `PackageReference`s) is per-project — it does not propagate through a `ProjectReference` to a consuming project. `SelfTests` carries no `HotChocolate.Data` reference itself, so bare unqualified `Result<T>` compiles cleanly there even though it references `SharedKernel.Testing` (which does have the ambiguity internally). Don't reflexively fully-qualify `Result<T>` in `SelfTests` test files — check the consuming project's own package references first.
