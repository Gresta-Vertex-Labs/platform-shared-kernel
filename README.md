# Platform.SharedKernel

The shared building blocks of a .NET 10 microservice platform, published as NuGet packages: primitives
and the execution context, DDD building blocks, the request pipeline and its mediator adapter,
persistence (PostgreSQL), messaging (MassTransit), caching, storage, search, security, service
defaults, test packages and more. No business logic lives here.

Every package ships at **one version**. A single `v*` tag publishes all of them together, so any
version that exists for one package exists for every package.

- Architecture and package map: [`CLAUDE.md`](CLAUDE.md)
- Build, versioning, CI and releases: [`PLATFORM.md`](PLATFORM.md)
- Working services built on the packages: [`samples/`](samples/)

## Using the packages in a service

Name the kernel version once, in your `Directory.Packages.props`, and point every `SharedKernel.*`
package at it. Upgrading is a one-line change, and the packages can never end up at mixed versions.

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <!-- The one SharedKernel release this service builds against. -->
    <SharedKernelVersion>1.0.0</SharedKernelVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="SharedKernel.Primitives" Version="$(SharedKernelVersion)" />
    <PackageVersion Include="SharedKernel.Execution" Version="$(SharedKernelVersion)" />
    <PackageVersion Include="SharedKernel.Application" Version="$(SharedKernelVersion)" />
    <PackageVersion Include="SharedKernel.ServiceDefaults" Version="$(SharedKernelVersion)" />
    <!-- One line per SharedKernel package you reference, always $(SharedKernelVersion). -->
  </ItemGroup>
</Project>
```

The packages are on GitHub Packages, which needs a token with `read:packages` even for reads. Map
`SharedKernel.*` to that feed in your `NuGet.Config`:

```xml
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="sharedkernel" value="https://nuget.pkg.github.com/Gresta-Vertex-Labs/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="sharedkernel"><package pattern="SharedKernel.*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
  <packageSourceCredentials>
    <sharedkernel>
      <add key="Username" value="%GITHUB_ACTOR%" />
      <add key="ClearTextPassword" value="%GITHUB_TOKEN%" />
    </sharedkernel>
  </packageSourceCredentials>
</configuration>
```

Don't pin one `SharedKernel.*` package to a different version from the rest, and don't float the
version (`*`). More detail is in [`PLATFORM.md`](PLATFORM.md), under "Consuming the kernel".

## Building this repository

Requirements: the .NET 10 SDK (10.0.300 or newer, see [`global.json`](global.json)). The
Integration lane also needs Docker.

```bash
dotnet build Platform.SharedKernel.slnx -c Release
dotnet test  Platform.SharedKernel.Unit.slnf -c Release --no-build          # no Docker needed
dotnet test  Platform.SharedKernel.Integration.slnf -c Release --no-build   # Testcontainers
dotnet pack  Platform.SharedKernel.slnx -c Release --no-build -o artifacts/packages
eng/verify-packages.sh artifacts/packages   # the release set check; needs a folder holding one pack only
```

## Releasing

Pushing a `v<major>.<minor>.<patch>[-prerelease]` tag on a commit on `main` starts
[`release.yml`](.github/workflows/release.yml). It runs every gate: the tier check, the full build,
and the Unit and Integration suites. It packs every package and checks that none is missing and all
carry the tag's version. Then it runs every consumer harness and sample against those packages.
Only then does it publish exactly those package files. There is no way to publish a single
package: [`publish-package.yml`](.github/workflows/publish-package.yml) is a dry run.
