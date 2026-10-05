# Package README standard

Every packable `SharedKernel.*` project ships its `README.md` inside the NuGet package (`PackageReadmeFile`), so the
same file is read in three places: on GitHub, on the package feed, and in the IDE's package manager. This standard keeps
all of them consistent, scannable and correct. `PackageReadmeStandardTests` (in
`tools/Governance/SharedKernel.ArchitectureTests`) enforces the mechanical parts.

## Rules

1. **Order is fixed.** Sections appear in the order below. Sections marked *optional* are left out when the package
   has nothing to say there. Never rename a required heading.
2. **Links are absolute.** Package feeds do not resolve relative links. Link with
   `https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/<path>`. Never link to `CLAUDE.md`,
   `state-map.md` or `docs/`: they are maintainer material, not package documentation.
3. **Code is real.** Every snippet uses the package's current public API (check `PublicAPI.Shipped.txt`), with the
   `using` lines it needs. Where a `ReadmeSample*Tests.cs` exists, it mirrors the snippets.
4. **Configuration is a table.** Every options key a consumer can set appears in the Configuration table with its full
   section path, type, default and meaning.
5. **Length follows substance.** A small package is 80–150 lines; a large one should stay under about 500. Deep
   background belongs in the domain README.
6. **Write for the reader who has 30 seconds.** The callout and the "You get / So that" table must answer "what is
   this and why would I use it" on their own.

## Section order

| # | Section | Required | Content |
|---|---------|----------|---------|
| 1 | `# SharedKernel.X` | yes | The package id, exactly. |
| 2 | Badges | yes | .NET 10 · License MIT · **Tier** · Public API tracked (when it is) · optional behaviour badge. |
| 3 | Callout | yes | A bold one- or two-sentence blockquote: what the package does and the problem it removes. |
| 4 | You get / So that | yes | 3–8 rows: capability → the benefit to the consumer. |
| 5 | `## Contents` | when > ~150 lines | Links to the `##` sections. |
| 6 | `## Install` | yes | The `PackageReference`, the `SharedKernelVersion` note, a requirements table (tier, the service project it belongs in, depends on, namespaces). |
| 7 | `## Quick start` | yes | The registration call, the `appsettings.json` section, one usage snippet. Runnable in under five minutes. |
| 8 | `## How it works` | optional | A mermaid diagram and short bullets on the behaviour that matters (ordering, failure, tenancy, retries). |
| 9 | `## Recipes` | optional | Numbered, task-named `###` subsections: "1. Rotate a key without downtime". |
| 10 | `## Configuration` | when the package has options | `Key` · `Type` · `Default` · `Meaning`, keys shown with their full section path. |
| 11 | `## Reference` | yes | Registration methods, the main services, error codes, log events (EventId, level, message), the readiness probe name. |
| 12 | `## Testing` | yes | The `SharedKernel.*.Testing` package and fakes to use in a service's tests, or how to test against it. |
| 13 | `## Pitfalls` | yes | A `Don't` · `Do` · `Why` table. |
| 14 | `## Design decisions` | optional | "Why X?" paragraphs for choices a reader would otherwise question. |
| 15 | Footer | yes | A rule (`---`) and the "Part of Platform.SharedKernel" line. |

## Skeleton

Copy this and fill it in. Delete optional sections you do not need.

````markdown
# SharedKernel.X

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Adapter](https://img.shields.io/badge/tier-Adapter-6f42c1)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

> **One or two sentences: what this package does, and the problem it takes off your hands.**

| You get | So that |
| --- | --- |
| `AddX(configuration)` | One call wires the feature, validated at startup |
| … | … |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [Configuration](#configuration)
- [Reference](#reference)
- [Testing](#testing)
- [Pitfalls](#pitfalls)

## Install

```xml
<PackageReference Include="SharedKernel.X" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Adapter — reference it from your **Infrastructure** project |
| Depends on | `SharedKernel.X.Abstractions` |
| Namespaces | `SharedKernel.X` |

## Quick start

```csharp
using SharedKernel.X;

builder.Services.AddX(builder.Configuration);   // SharedKernel:X
```

```json
{
  "SharedKernel": {
    "X": { "Setting": "value" }
  }
}
```

```csharp
public sealed class Example(IX x)
{
    public Task<Result> RunAsync(CancellationToken ct) => x.DoAsync(ct);
}
```

## How it works

## Recipes

### 1. Task-named recipe

## Configuration

Section `SharedKernel:X`, validated when the host starts.

| Key | Type | Default | Meaning |
| --- | --- | --- | --- |
| `SharedKernel:X:Setting` | `string` | — (required) | … |

## Reference

### Registration

| Method | Registers |
| --- | --- |
| `AddX(IConfiguration)` | `IX` (singleton) |

### Errors

| Code | Type | When |
| --- | --- | --- |
| `x.not_found` | NotFound | … |

### Logging

| Event id | Level | Event |
| --- | --- | --- |
| 9000 | Warning | … |

### Health

Registers the `x` readiness probe; `AddSharedKernelReadiness()` exposes it on `/health/ready`.

## Testing

Reference [`SharedKernel.X.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Testing/SharedKernel.X.Testing/README.md)
from your test project and call `services.AddFakeX()`.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| … | … | … |

## Design decisions

**Why …?** …

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) ·
[X domain](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/NN.X/README.md) ·
[MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
````

## Badge reference

| Badge | Markdown |
| --- | --- |
| Tier: Foundation | `![Tier: Foundation](https://img.shields.io/badge/tier-Foundation-2ea44f)` |
| Tier: Model | `![Tier: Model](https://img.shields.io/badge/tier-Model-0969da)` |
| Tier: Abstractions | `![Tier: Abstractions](https://img.shields.io/badge/tier-Abstractions-1f6feb)` |
| Tier: Adapter | `![Tier: Adapter](https://img.shields.io/badge/tier-Adapter-6f42c1)` |
| Tier: Host | `![Tier: Host](https://img.shields.io/badge/tier-Host-d73a49)` |
| Tier: Testing | `![Tier: Testing](https://img.shields.io/badge/tier-Testing-e36209)` |
| Tier: Tooling | `![Tier: Tooling](https://img.shields.io/badge/tier-Tooling-6a737d)` |

## Which service project takes the package

| Tier | Service project |
| --- | --- |
| Foundation | Any project |
| Model | **Domain** |
| Abstractions | **Application** |
| Adapter | **Infrastructure** |
| Host | **Api** / **Worker** |
| Testing | Test projects only |
| Tooling | Build (analyzers, generators) |
