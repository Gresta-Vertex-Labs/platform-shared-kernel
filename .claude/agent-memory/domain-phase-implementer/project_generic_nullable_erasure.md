---
name: project_generic_nullable_erasure
description: C# generic type-parameter nullable-annotation erasure gotcha — TKey? erases to plain TKey for value-type closures unless TKey has a struct constraint
type: project
---

When a generic type parameter is constrained only by an interface (e.g. `where TKey : IComparable<TKey>`, no `struct`/`class` split), writing `TKey?` in the open generic definition does NOT produce `Nullable<TKey>` when the closed type is a value type (e.g. `TKey = DateTimeOffset`) — the compiler erases `TKey?` to plain non-nullable `TKey` for that closure. This is confirmed empirically (not from memory/docs) via a real `dotnet build` error: passing an actual `DateTimeOffset?` argument to a parameter the compiler resolved as bare `DateTimeOffset`.

**Why:** `Nullable<T>` requires `T : struct` at the CLR level. Without that constraint on the open type parameter, the compiler can't guarantee a value-type closure gets `Nullable<T>` semantics, so the "unconstrained nullable type parameter" annotation feature (C# 9+) silently degrades to a no-op for value-type instantiations — it only does something meaningful for reference-type closures.

**How to apply:** Any time you're designing a generic type with a "may be absent" value of an open type parameter (e.g. a cursor/keyset-pagination `TKey? AfterKey`), and you want that to genuinely support value-typed closures with real null semantics, add `where TKey : struct` explicitly (accepting the trade-off that reference-typed closures like `string` are then excluded), or represent absence via a separate `bool`/`object?` companion instead of relying on bare `TKey?`. Encountered and fixed in `03.Domain`'s `KeysetSpecification<T, TKey>` (WO-051/C-39) — the domain-arch-planner's original design (`where TKey : IComparable<TKey>` only) didn't compile as intended; the phase-implementer corrected it to `where TKey : struct, IComparable<TKey>` and documented the correction back into `03.Domain/CLAUDE.md`. Worth checking for on any future generic "nullable cursor/optional key" design across other domains before it ships.
