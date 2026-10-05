#!/usr/bin/env bash
# Proves the tier check (eng/SharedKernelTiers.targets) still FAILS the build (WO-086 / P-574):
#
#   eng/verify-tier-errors.sh
#
# The normal build only shows that the repository has no tier violation. It cannot show that a new one
# would be rejected: a downgrade to a warning, a wrong condition or a target that stops running would
# all leave it green. This script builds two throw-away projects that each break one rule and fails
# unless both builds fail with the expected error code:
#   - a Model project referencing an Abstractions project           -> error SKTIER001
#   - an Adapter project taking the Microsoft.AspNetCore.App framework -> error SKTIER006
# The probes live under eng/.tier-probe (inside the repository, so Directory.Build.* apply) and are
# deleted on exit.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"
probe_root="eng/.tier-probe"
rm -rf "$probe_root"
trap 'rm -rf "$repo_root/$probe_root"' EXIT

failed=0

# probe <name> <tier> <item group xml> <expected code>
probe() {
  local name="$1" tier="$2" items="$3" code="$4"
  local dir="$probe_root/$name"
  mkdir -p "$dir"
  cat > "$dir/$name.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <SharedKernelTier>$tier</SharedKernelTier>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
$items
  </ItemGroup>
</Project>
EOF
  echo "namespace TierProbe; public static class Probe;" > "$dir/Probe.cs"

  local log="$dir/build.log" status=0
  dotnet build "$dir/$name.csproj" -c Release -p:BuildProjectReferences=false -nologo > "$log" 2>&1 || status=$?

  if [ "$status" -eq 0 ]; then
    echo "::error::$name built successfully; expected it to fail with $code."
    failed=1
  elif ! grep -q "error $code" "$log"; then
    echo "::error::$name failed, but not with 'error $code':"
    grep -E "error|warning $code" "$log" | sed 's/^/  /' | head -20
    failed=1
  else
    echo "OK: $name fails with error $code."
  fi
}

probe ModelReferencesAbstractions Model \
  '    <ProjectReference Include="../../../src/Infrastructure/Caching/SharedKernel.Caching.Abstractions/SharedKernel.Caching.Abstractions.csproj" />' \
  SKTIER001

probe AdapterReferencesAspNetCore Adapter \
  '    <FrameworkReference Include="Microsoft.AspNetCore.App" />' \
  SKTIER006

exit "$failed"
