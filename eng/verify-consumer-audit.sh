#!/usr/bin/env bash
# Consumer-view vulnerable-package gate (P-636). Restores every packable SharedKernel package the
# way a consuming service would, alone, from outside the repository, with the full NuGet audit on,
# and fails when any restore reports a known vulnerability (NU1901-NU1904) or could not run the
# audit at all (NU1900). Used by .github/workflows/verify.yml (packaging-verify, against the packed
# set) and .github/workflows/consumer-audit.yml (scheduled, against the published feed), and
# runnable locally:
#
#   eng/verify-consumer-audit.sh <source> <version>
#
#   <source>   a folder of packed .nupkg files, or a NuGet v3 feed URL. For the GitHub Packages feed
#              the restore authenticates with GITHUB_ACTOR / GITHUB_TOKEN from the environment, the
#              same as a consuming service (CONTRIBUTING.md, "Consuming the packages").
#   <version>  the one version every package is restored at.
#
# Why a separate gate. The in-repo restore (verify.yml, "NuGet audit") sees this repository's
# build graph: a <GlobalPackageReference> or Central Package Management pin fixes a vulnerable
# transitive for the repository's own builds, yet never reaches a consumer, because it is not in the
# packed .nuspec. A consumer restores the .nuspec dependency graph as shipped. This script is that
# view. No Directory.Build.props, Directory.Packages.props or NuGet.Config of the repository is
# read: the work folder is outside it and carries its own NuGet.Config. Transitive pinning stays off
# in the repository; a finding is fixed by an explicit reference in the owning packable project
# (eng/README.md, "Directory.Packages.props").
#
# The set is derived, never listed by hand: eng/PackageInventory.proj asks every csproj whether it
# packs (EXPECTED_PACKAGES_FILE reuses an earlier inventory, as in verify-packages.sh).
#
# A negative control runs with every gate: an old, known-vulnerable Newtonsoft.Json must produce
# NU1903. If it does not, the audit is not actually working (vulnerability data unreachable, mode
# changed) and a green result would prove nothing, so the gate fails.
#
# CONSUMER_AUDIT_EXTRA="Id@Version [Id@Version ...]" adds cases that are judged like shipped
# packages. It exists to show locally that the gate fails on a bad reference; CI never sets it.
set -euo pipefail

# Warning text is localized by the SDK; the NU codes the script matches are not, but keep the log readable.
export DOTNET_CLI_UI_LANGUAGE=en VSLANG=1033

source_arg="${1:?usage: eng/verify-consumer-audit.sh <package-folder-or-feed-url> <version>}"
version="${2:?usage: eng/verify-consumer-audit.sh <package-folder-or-feed-url> <version>}"
version="${version#v}"

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

native() { if command -v cygpath > /dev/null 2>&1; then cygpath -m "$1"; else printf '%s' "$1"; fi; }

control_id="Newtonsoft.Json"
control_version="12.0.1"
control_case="control.${control_id}"

# 1. The packable set.
if [ -n "${EXPECTED_PACKAGES_FILE:-}" ]; then
  tr -d '\r' < "$EXPECTED_PACKAGES_FILE" | sed '/^[[:space:]]*$/d' | sort -u > "$work/packages.txt"
else
  dotnet msbuild "$repo_root/eng/PackageInventory.proj" -nologo -v:q -p:InventoryFile="$(native "$work")/inventory.txt"
  tr -d '\r' < "$work/inventory.txt" | sed '/^[[:space:]]*$/d' | sort -u > "$work/packages.txt"
fi
package_count=$(wc -l < "$work/packages.txt")
if [ "$package_count" -eq 0 ]; then
  echo "::error::The packable package set is empty."
  exit 1
fi

# 2. The consumer's NuGet.Config: SharedKernel.* from <source> only, everything else from nuget.org.
if [ -d "$source_arg" ]; then
  source_value="$(native "$(cd "$source_arg" && pwd)")"
  credentials=""
  source_label="folder $source_arg"
else
  source_value="$source_arg"
  credentials='<packageSourceCredentials><sharedkernel><add key="Username" value="%GITHUB_ACTOR%" /><add key="ClearTextPassword" value="%GITHUB_TOKEN%" /></sharedkernel></packageSourceCredentials>'
  source_label="feed $source_arg"
  : "${GITHUB_ACTOR:?GITHUB_ACTOR must be set to read a feed}"
  : "${GITHUB_TOKEN:?GITHUB_TOKEN must be set to read a feed}"
fi

cat > "$work/NuGet.Config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="sharedkernel" value="$source_value" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="sharedkernel"><package pattern="SharedKernel.*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
  $credentials
</configuration>
EOF

# 3. One consumer project per package (plus the control and any extras), one solution over all of
#    them so a single restore audits them in parallel. Project files are named consume.<case>.csproj
#    so no project shares a name with the package it references.
write_case() { # <case> <package id> <package version>
  mkdir -p "$work/cases/$1"
  cat > "$work/cases/$1/consume.$1.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <NuGetAudit>true</NuGetAudit>
    <NuGetAuditMode>all</NuGetAuditMode>
    <NuGetAuditLevel>low</NuGetAuditLevel>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="$2" Version="$3" />
  </ItemGroup>
</Project>
EOF
  printf '  <Project Path="cases/%s/consume.%s.csproj" />\n' "$1" "$1" >> "$work/projects.txt"
}

: > "$work/projects.txt"
while IFS= read -r id; do write_case "$id" "$id" "$version"; done < "$work/packages.txt"
write_case "$control_case" "$control_id" "$control_version"
extra_cases=()
for extra in ${CONSUMER_AUDIT_EXTRA:-}; do
  write_case "extra.${extra%@*}" "${extra%@*}" "${extra#*@}"
  extra_cases+=("extra.${extra%@*}")
done
{ echo '<Solution>'; cat "$work/projects.txt"; echo '</Solution>'; } > "$work/consumers.slnx"

echo "Restoring $package_count packages at $version from $source_label as a consumer, NuGetAuditMode=all..."
start=$SECONDS
status=0
# Outside the repository on purpose: nothing of its build files applies.
(cd "$work" && dotnet restore consumers.slnx --force --configfile NuGet.Config -nologo -v:q) > "$work/restore.log" 2>&1 || status=$?
[ -z "${CONSUMER_AUDIT_LOG:-}" ] || cp "$work/restore.log" "$CONSUMER_AUDIT_LOG"
echo "Restore finished in $((SECONDS - start))s (exit $status)."

# 4. Findings: "<csproj> : warning NU1903: Package 'X' 1.0 has a known high severity vulnerability, <url>
#    [<csproj>]", once per project (the summary repeats them; sort -u collapses that).
sed 's/\r$//' "$work/restore.log" | grep -E 'NU190[0-4]' | sort -u > "$work/findings.txt" || true

case_of() { # the case folder of "<work>/cases/<case>/consume.<case>.csproj : warning ..." (either slash)
  sed -nE 's#^.*cases[/\\]([^/\\]+)[/\\]consume\..*\.csproj :.*$#\1#p' <<< "$1" | head -n1
}

failed=0
control_hit=0
declare -A offenders=()
while IFS= read -r line; do
  [ -n "$line" ] || continue
  case_name="$(case_of "$line")"; case_name="${case_name:-unattributed}"
  detail="$(sed -E 's/^.*(NU190[0-4]): /\1 /; s/ \[[^]]*\]$//' <<< "$line")"
  if [ "$case_name" = "$control_case" ]; then
    case "$line" in *NU1901*|*NU1902*|*NU1903*|*NU1904*) control_hit=1 ;; esac
    continue
  fi
  offenders["$case_name"]+="$detail"$'\n'
  failed=1
done < "$work/findings.txt"

for case_name in "${!offenders[@]}"; do
  subject="$case_name"
  case "$case_name" in extra.*) subject="${case_name#extra.} (extra probe)" ;; esac
  echo "::error::Consumers of $subject restore a vulnerable or unaudited dependency:"
  # shellcheck disable=SC2001
  sed 's/^/  /' <<< "${offenders[$case_name]%$'\n'}"
done

if [ "$status" -ne 0 ] && [ "$failed" -eq 0 ]; then
  echo "::error::dotnet restore failed (exit $status) without an audit finding:"
  grep -E 'error |NU[0-9]{4}' "$work/restore.log" | sed 's/\r$//' | sort -u | head -40
  failed=1
fi

if [ "$control_hit" -eq 0 ]; then
  echo "::error::The negative control ($control_id $control_version, a known-vulnerable package) produced no NU1901-NU1904 warning, so the audit is not running and a pass would prove nothing. Check that the vulnerability data of nuget.org is reachable and NuGetAuditMode is 'all'."
  failed=1
fi

if [ "$failed" -ne 0 ]; then
  echo "::error::Consumer-view audit failed. Fix a vulnerable transitive with an explicit patched reference in the owning packable project (eng/README.md, 'Directory.Packages.props'); a GlobalPackageReference never reaches consumers."
  exit 1
fi

echo "OK: all $package_count packages restore at $version with no NU1901-NU1904 finding (negative control fired)."
