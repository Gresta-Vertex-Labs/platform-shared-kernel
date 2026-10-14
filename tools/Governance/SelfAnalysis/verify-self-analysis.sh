#!/usr/bin/env bash
# Proves the kernel self-analysis wiring end to end:
#   1. the probe (one deliberate violation per rule) fails to build and every finding is an error, with and without
#      TreatWarningsAsErrors;
#   2. a real src/ project, built through the root Directory.Build.targets, compiles with kernel.globalconfig applied;
#   3. with --pack <csproj>: the self-analysis import leaves no analyzer dependency in the packed .nuspec.
# Usage: tools/Governance/SelfAnalysis/verify-self-analysis.sh [--pack <path-to-csproj>]
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
probe="$root/tools/Governance/_verification/SelfAnalysisProbe"
expected=(SK0001 SK0022 SK0043 SK0044 CA2200)

build() {
  dotnet build "$probe" -c Release --no-incremental -nologo -v:minimal "$@" 2>&1 || true
}

echo "== probe: every rule is an error =="
for twae in true false; do
  output="$(build -p:TreatWarningsAsErrors=$twae)"
  for id in "${expected[@]}"; do
    grep -q "error $id" <<<"$output" || { echo "FAIL: $id is not an error (TreatWarningsAsErrors=$twae)"; echo "$output"; exit 1; }
  done
  grep -q "warning \(SK\|CA2200\)" <<<"$output" && { echo "FAIL: a rule is still a warning (TreatWarningsAsErrors=$twae)"; echo "$output"; exit 1; }
done
echo "OK: five findings, all errors, with and without TreatWarningsAsErrors"

echo "== a src project through the root import =="
sample="$root/src/Hosting/Security/SharedKernel.Security.ApiKey/SharedKernel.Security.ApiKey.csproj"
sim_output="$(dotnet build "$sample" -c Release --no-incremental -nologo -v:detailed 2>&1 || true)"
grep -q "kernel.globalconfig" <<<"$sim_output" || { echo "FAIL: kernel.globalconfig did not reach the compiler of a src project"; exit 1; }
grep -q "[1-9][0-9]* Error(s)\|[1-9][0-9]* Hata" <<<"$sim_output" && { echo "FAIL: the src project does not build with the rules as errors"; exit 1; }
echo "OK: a src project builds with kernel.globalconfig applied"

if [ "${1:-}" = "--pack" ]; then
  project="${2:?--pack needs a csproj}"
  out="$(mktemp -d)"
  dotnet pack "$project" -c Release -nologo -v:minimal -p:PackageOutputPath="$out" >/dev/null
  nuspec="$(unzip -p "$out"/*.nupkg '*.nuspec')"
  if grep -qi "SharedKernel.Analyzers" <<<"$nuspec"; then
    echo "FAIL: the packed .nuspec depends on SharedKernel.Analyzers"; exit 1
  fi
  echo "OK: the packed .nuspec carries no analyzer dependency"
fi
