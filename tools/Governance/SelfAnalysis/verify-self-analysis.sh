#!/usr/bin/env bash
# Proves the kernel self-analysis wiring end to end, without editing the root Directory.Build.targets:
#   1. report mode: the probe (TreatWarningsAsErrors=true, one deliberate violation per rule) builds, the five
#      findings are warnings, and a SARIF file with them is written;
#   2. enforce mode: the same probe fails and every finding is an error;
#   3. root-import simulation: a real src/ project, built with a stand-in Directory.Build.targets that imports the root one
#      plus the self-analysis props (exactly the one line devops-lead will add), reports SK0044 from the severity file and
#      still builds in report mode;
#   4. with --pack <csproj>: the self-analysis import leaves no analyzer dependency in the packed .nuspec.
# Usage: tools/Governance/SelfAnalysis/verify-self-analysis.sh [--pack <path-to-csproj>]
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
root_native="$(cd "$root" && (pwd -W 2>/dev/null || pwd))"
probe="$root/tools/Governance/_verification/SelfAnalysisProbe"
expected=(SK0001 SK0022 SK0043 SK0044 CA2200)

build() {
  dotnet build "$probe" -c Release --no-incremental -nologo -v:minimal "$@" 2>&1 || true
}

echo "== report mode =="
report_output="$(build)"
for id in "${expected[@]}"; do
  grep -q "warning $id" <<<"$report_output" || { echo "FAIL: $id is not a warning in report mode"; echo "$report_output"; exit 1; }
done
grep -q "error " <<<"$report_output" && { echo "FAIL: report mode produced an error"; echo "$report_output"; exit 1; }
sarif="$root/artifacts/bin/SelfAnalysisProbe/release/sharedkernel-observability.sarif"
[ -f "$sarif" ] || { echo "FAIL: no SARIF file at $sarif"; exit 1; }
for id in "${expected[@]}"; do
  grep -q "\"ruleId\": \"$id\"" "$sarif" || { echo "FAIL: $id missing from the SARIF file"; exit 1; }
done
echo "OK: five findings, all warnings, SARIF written"

echo "== enforce mode =="
enforce_output="$(build -p:SharedKernelObservabilityMode=enforce)"
for id in "${expected[@]}"; do
  grep -q "error $id" <<<"$enforce_output" || { echo "FAIL: $id is not an error in enforce mode"; echo "$enforce_output"; exit 1; }
done
echo "OK: five findings, all errors"

echo "== root-import simulation =="
standin="$(mktemp -d)/Directory.Build.targets"
cat >"$standin" <<XEOF
<Project>
  <Import Project="$root_native/Directory.Build.targets" />
  <Import Project="$root_native/tools/Governance/SelfAnalysis/SharedKernel.SelfAnalysis.props" />
</Project>
XEOF
sample="$root/src/Hosting/Security/SharedKernel.Security.ApiKey/SharedKernel.Security.ApiKey.csproj"
sim_output="$(dotnet build "$sample" -c Release --no-incremental -nologo -v:minimal -p:DirectoryBuildTargetsPath="$standin" 2>&1 || true)"
grep -q "warning SK0044" <<<"$sim_output" || { echo "FAIL: SK0044 (enabled by the severity file) did not fire in a src project"; echo "$sim_output"; exit 1; }
grep -q "error " <<<"$sim_output" && { echo "FAIL: the src project failed to build in report mode"; echo "$sim_output"; exit 1; }
echo "OK: a src project builds in report mode with the severity file applied"

if [ "${1:-}" = "--pack" ]; then
  project="${2:?--pack needs a csproj}"
  out="$(mktemp -d)"
  dotnet pack "$project" -c Release -nologo -v:minimal -p:DirectoryBuildTargetsPath="$standin" -p:PackageOutputPath="$out" >/dev/null
  nuspec="$(unzip -p "$out"/*.nupkg '*.nuspec')"
  if grep -qi "SharedKernel.Analyzers" <<<"$nuspec"; then
    echo "FAIL: the packed .nuspec depends on SharedKernel.Analyzers"; exit 1
  fi
  echo "OK: the packed .nuspec carries no analyzer dependency"
fi
