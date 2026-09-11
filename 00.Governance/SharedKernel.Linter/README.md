# SharedKernel.Linter

Formatting enforcement for the SharedKernel platform. **Ships no DLL and no analyzers.** It gives a
consuming service three things: a format check that fails CI on unformatted code, a one-command
local format that needs no tool install, and the platform's shared `.editorconfig`.

A formatter only settles style arguments if it is mechanical. This package is how a service gets
the platform's formatting enforced on its pull requests without hand-rolling an MSBuild target or
pinning a formatter version itself.

## Install

```xml
<PackageReference Include="SharedKernel.Linter" Version="1.0.0" PrivateAssets="all" />
```

It declares itself a development dependency, so it never reaches your published package's
dependency graph; `PrivateAssets="all"` is belt-and-braces and harmless.

That reference alone gives you the CI check. Then install the shared style config once, from your
repository root so it covers every project beneath it, and **commit it**:

```bash
dotnet build -t:InstallSharedKernelLinterConfig -p:SharedKernelLinterConfigDestination=.
```

An existing `.editorconfig` is never overwritten, so your local edits are safe. To take an updated
version of the platform config later, ask for it explicitly:

```bash
dotnet build -t:InstallSharedKernelLinterConfig -p:SharedKernelLinterOverwriteConfig=true
```

## Day-to-day use

Format before you commit. No global tool, no `dotnet-tools.json`:

```bash
dotnet build -t:SharedKernelLinterFormat
```

This runs the exact CSharpier version CI will check against — it came with the package. A
separately-installed `csharpier` is the classic way to end up with a machine that formats one way
and a pipeline that demands another.

| Situation | Behaviour |
|---|---|
| Local build (`dotnet build`, any configuration) | Nothing. No check, no reformatting. |
| `-t:SharedKernelLinterFormat` | Formats this project's files in place. |
| CI build (`ContinuousIntegrationBuild=true`) | Check only; unformatted files fail the build. |
| Any build | Your files are never rewritten behind your back. |

One property controls enforcement:

```bash
# reproduce a CI formatting failure locally
dotnet build -p:SharedKernelLinterEnforceFormatting=true
```

```xml
<!-- opt out, per project or repo-wide in Directory.Build.props -->
<SharedKernelLinterEnforceFormatting>false</SharedKernelLinterEnforceFormatting>
```

The default is `$(ContinuousIntegrationBuild)`, which GitHub Actions, Azure Pipelines and most
other providers set for you. Keeping the check out of local builds is deliberate: a formatter that
interrupts you mid-thought is a formatter people learn to bypass.

## One config file, on purpose

The package ships `.editorconfig` and **no `.csharpierrc.json`**. That is not an omission.

CSharpier reads `indent_style`, `indent_size`, `end_of_line` and `max_line_length` straight from
`.editorconfig` — but if a `.csharpierrc` exists it uses that **instead**, not merged. So shipping
both would mean editing `indent_size` or `max_line_length` in `.editorconfig` and watching nothing
happen. One file drives the IDE, the Roslyn code-style analyzers, and the formatter.

If you genuinely need a CSharpier-only option, adding your own `.csharpierrc` works — just know
that it then takes over the formatting keys entirely.

### Why the config is copied rather than applied

Both the config and the formatter need the file inside **your** source tree:

- EditorConfig matches its sections relative to the directory containing the file, so an
  `.editorconfig` sitting in the NuGet cache matches none of your sources.
- CSharpier resolves its settings by walking up from each file it formats, with the same result.

No `PackageReference` mechanism writes files into a consuming project's source tree —
`contentFiles` are surfaced as links resolved from the cache, and `content/` is
packages.config-era and ignored outright. So a package cannot make the config apply on your
behalf. Copying is the only thing that works, and doing it explicitly beats doing it behind your
back on first build.

The practical consequence: your committed copy **can** drift from the platform's. The CI check is
what holds the line, because the formatter and its version come from this package.

## What the config covers

`.editorconfig` is the payload, and it is split by who owns what:

| Area | Owner |
|---|---|
| Layout — where lines break and wrap | CSharpier. The `csharp_new_line_*` / `csharp_space_*` keys match its output so IDE typing agrees. |
| `max_line_length` | The single source of the formatter's print width (120). |
| Naming conventions | EditorConfig — `I`-prefixed interfaces, `T`-prefixed type parameters, `_camelCase` private fields, PascalCase constants and members, camelCase parameters and locals. |
| Language style | EditorConfig — file-scoped namespaces, required braces, pattern matching over cast-checks, null propagation, no `this.` qualification, `var` where the type is apparent. |
| Nullable diagnostics | EditorConfig — the null-state warnings raised from their defaults so they do not blend into build output. |

Severities are **warnings, not errors**. A shared config that fails other teams' builds over a
style opinion gets deleted rather than adopted; escalate what you care about in your own
`.editorconfig`, which layers on top of the installed copy. Test projects are exempted from the
member-naming rule, because underscores in test names are how a test states its scenario.

## Before you turn the check on

**CSharpier 1.x formats XML as well as C#**, so the check covers `.csproj`, `.props` and
`.targets` alongside `.cs`. On a codebase that has never run it, expect a large first pass —
measure it before you enable anything:

```bash
dotnet build -t:SharedKernelLinterFormat   # then review the diff
```

The sane adoption order is: install the config, run the format target once as a single reviewable
commit, then let CI keep it that way.

## Contents

| Path in package | Purpose |
|---|---|
| `build/SharedKernel.Linter.props` | Decides whether the check runs; pins CSharpier to report-only |
| `build/SharedKernel.Linter.targets` | `SharedKernelLinterFormat` and `InstallSharedKernelLinterConfig` |
| `build/config/.editorconfig` | The shared style configuration |

`build/` at the package root is the only location NuGet auto-imports for a `PackageReference`.
`buildTransitive/` is deliberately not used: a formatting gate should never appear in someone's
build because they referenced a library that referenced this.

## Requirements

- .NET SDK 8.0 or later (CSharpier resolves its own runtime)
- One dependency: `CSharpier.MsBuild` (MIT), which performs the cross-platform formatter
  resolution and the check itself

## License

MIT — part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel).
See the [00.Governance README](../README.md) for the rest of the governance toolchain:
`SharedKernel.Analyzers` (compile-time rules) and `SharedKernel.ArchitectureTests` (layering rules).
