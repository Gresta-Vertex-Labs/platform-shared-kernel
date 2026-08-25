# SharedKernel.Linter

Content-only NuGet package distributing Platform.SharedKernel's shared formatting configuration. **Ships no DLL and no analyzers** — it delivers config files and MSBuild targets. Reference with `PrivateAssets="all"`.

## Included

| File | Purpose |
|---|---|
| `.editorconfig` | Shared C# style, naming, and formatting rules for the whole platform |
| `.csharpierrc.json` | CSharpier formatter configuration |
| `build/SharedKernel.Linter.props` | Imported early — wires the config into the consuming project |
| `build/SharedKernel.Linter.targets` | Build-time CSharpier formatting enforcement |

Referencing this package is how a consuming service inherits the platform's formatting conventions without copying config files that then drift.

## Quick Start

```xml
<PackageReference Include="SharedKernel.Linter" Version="1.0.0" PrivateAssets="all" />
```

The props/targets import automatically. `PrivateAssets="all"` is required — a formatting toolchain must never become a transitive dependency of your published package.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see the [00.Governance README](../README.md) for the full governance toolchain, including `SharedKernel.Analyzers` (compile-time rules) and `SharedKernel.ArchitectureTests` (layering rules).
