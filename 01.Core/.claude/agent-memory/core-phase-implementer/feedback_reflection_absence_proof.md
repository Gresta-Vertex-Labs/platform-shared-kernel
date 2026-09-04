---
name: feedback-reflection-absence-proof
description: How to prove "no reflection in this package" rigorously — compiled-assembly PE/metadata scan, not a source grep, confirmed during P-474 (SharedKernel.DataPrivacy)
type: feedback
---

When a phase spec asks for a test proving a package uses no reflection (e.g. P-474/T-58's
`PiiMasking`/attribute package), a source-code grep for "reflection" is explicitly called out as
weak — it only proves nobody typed the word, not that no reflection call compiled into the shipped
binary.

**What actually works**: open the compiled production DLL (`typeof(SomeProductionType).Assembly.Location`)
with `System.Reflection.Metadata`'s `PEReader`/`MetadataReader` (BCL, no extra NuGet needed — it's
part of the shared framework on .NET Core/.NET 5+) and enumerate `metadataReader.TypeReferences`.
Flag any `TypeReference` whose namespace is `System.Reflection` and whose name is NOT a pure
attribute type (`MethodInfo`/`PropertyInfo`/`FieldInfo`/`Assembly`/`BindingFlags`/etc. — anything
that indicates actual dynamic-invocation capability), rather than banning the whole namespace.

**Why the attribute exclusion is required, not a loophole**: this repo's `Directory.Build.props`
centralizes NuGet packaging metadata (Authors/Company/Product/Description/SourceLink), which makes
the .NET SDK auto-generate assembly-level attributes that live in `System.Reflection` themselves —
`AssemblyCompanyAttribute`, `AssemblyMetadataAttribute`, etc. Every SharedKernel package will fail
a blanket "no `System.Reflection.*` TypeReference at all" check purely from this SDK-generated
noise, unrelated to anything the package's own source does. Filtering out type names ending in
`"Attribute"` removes exactly that noise while still catching real invocation-surface types — and
even a `typeof(Foo).GetMethod(...)` call with no explicit `using System.Reflection;` still gets
caught, because the compiler must reference `MethodInfo` as the return type in the emitted
metadata regardless of how the call is written.

**Write a companion "the exclusion branch actually fires" test** — assert at least one
`System.Reflection.*Attribute` TypeReference IS present. Without it, the main assertion could be
vacuously true (the assembly has zero `System.Reflection` TypeReferences of any kind), and nobody
would notice the exclusion logic was never exercised.

**How to apply**: use this exact pattern (PEReader/MetadataReader + attribute-suffix exclusion +
companion non-vacuous-check test) any time a future `01.Core` phase needs to prove absence of
reflection, not the naive source-grep approach — grep is fine as a first pass but should never be
the only evidence offered when the spec explicitly asks for rigor.
