<#
.SYNOPSIS
  Builds the Shop against the kernel packed from this checkout (Windows PowerShell 5.1 or PowerShell 7).
.EXAMPLE
  samples/Shop/build.ps1            # pack the kernel, build the Shop
  samples/Shop/build.ps1 -NoPack    # reuse the packages already in nupkgs/
  samples/Shop/build.ps1 -Test      # also run the unit tests (no Docker)
  samples/Shop/build.ps1 -E2E       # also run the end-to-end flows (Docker; minutes)
#>
param([switch]$NoPack, [switch]$Test, [switch]$E2E)
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Push-Location $root
try {
    if (-not $NoPack) {
        Remove-Item nupkgs\SharedKernel.*.nupkg, nupkgs\SharedKernel.*.snupkg -ErrorAction SilentlyContinue
        dotnet pack Platform.SharedKernel.slnx -c Release -nologo -v:q
        if ($LASTEXITCODE -ne 0) { throw 'dotnet pack failed' }
    }

    $primitives = Get-ChildItem nupkgs\SharedKernel.Primitives.*.nupkg -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $primitives) { throw 'nupkgs/ holds no SharedKernel packages; run without -NoPack' }
    $version = $primitives.BaseName.Substring('SharedKernel.Primitives.'.Length)
    Write-Host "Shop builds against SharedKernel $version"

    # A repack at the same commit reuses the version number; drop that version from the NuGet cache so the new bits win.
    $cache = ((dotnet nuget locals global-packages --list) -replace '^[^:]*:\s*', '').Trim()
    Get-ChildItem $cache -Directory -Filter 'sharedkernel.*' -ErrorAction SilentlyContinue |
        ForEach-Object { Join-Path $_.FullName $version } |
        Where-Object { Test-Path $_ } |
        ForEach-Object { Remove-Item $_ -Recurse -Force }

    dotnet build samples\Shop\Shop.slnx -nologo "-p:SharedKernelPackageVersion=$version"
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed' }

    if ($Test -or $E2E) {
        dotnet test samples\Shop\Shop.slnx --no-build -nologo "-p:SharedKernelPackageVersion=$version"
        if ($LASTEXITCODE -ne 0) { throw 'unit tests failed' }
    }
    if ($E2E) {
        $env:SHOP_E2E = '1'
        try {
            dotnet test samples\Shop\Shop.E2E --no-build -nologo "-p:SharedKernelPackageVersion=$version"
            if ($LASTEXITCODE -ne 0) { throw 'end-to-end flows failed' }
        }
        finally { Remove-Item Env:SHOP_E2E }
    }
}
finally {
    Pop-Location
}
