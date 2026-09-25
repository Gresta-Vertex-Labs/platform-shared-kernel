#!/usr/bin/env bash
# Verifies a folder of packed .nupkg files is exactly one complete release-train package set
# (WO-086 / P-572). Used by .github/workflows/verify.yml (packaging-verify) and release.yml
# (publish), and runnable locally:
#
#   eng/verify-packages.sh <package-dir> [expected-version]
#
# Fails when:
#   - a package the repository is expected to ship is missing, or an unexpected one is present;
#   - the packages do not all carry one version, or that version is the 0.0.0 floor MinVer stamps
#     when it can see no git history;
#   - expected-version is given (a v-prefixed tag is accepted) and the packed version differs;
#   - the SharedKernel.* <PackageVersion> entries in Directory.Packages.props are not exactly the
#     expected set.
#
# The expected set is derived from the repository, never kept by hand: eng/PackageInventory.proj
# asks every csproj on disk whether it packs. Set EXPECTED_PACKAGES_FILE to reuse a list that was
# produced earlier (release.yml's publish job reuses the one packaging-verify uploaded, so it checks
# the downloaded artifact against the same set the verified run produced).
#
# On success, prints the version and, when GITHUB_ENV is set, exports it as SK_VERSION. Set
# WRITE_EXPECTED_TO to a path to also save the expected set there (uploaded next to the packages).
set -euo pipefail

package_dir="${1:?usage: eng/verify-packages.sh <package-dir> [expected-version]}"
expected_version="${2:-}"
expected_version="${expected_version#v}"

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

if [ ! -d "$package_dir" ]; then
  echo "::error::Package folder '$package_dir' does not exist."
  exit 1
fi

# 1. The expected set.
if [ -n "${EXPECTED_PACKAGES_FILE:-}" ]; then
  tr -d '\r' < "$EXPECTED_PACKAGES_FILE" | sed '/^[[:space:]]*$/d' | sort -u > "$work/expected.txt"
else
  dotnet msbuild "$repo_root/eng/PackageInventory.proj" -nologo -v:q -p:InventoryFile="$work/inventory.txt"
  tr -d '\r' < "$work/inventory.txt" | sed '/^[[:space:]]*$/d' | sort -u > "$work/expected.txt"
fi
expected_count=$(wc -l < "$work/expected.txt")
if [ "$expected_count" -eq 0 ]; then
  echo "::error::The expected package set is empty."
  exit 1
fi

# 2. What was packed. The version is anchored at the end of the file name, so a package id that
#    itself ends in a digit (SharedKernel.Storage.S3) is never misread as part of the version.
: > "$work/packed.txt"
: > "$work/versions.txt"
shopt -s nullglob
for nupkg in "$package_dir"/*.nupkg; do
  name="$(basename "$nupkg" .nupkg)"
  version="$(printf '%s\n' "$name" | grep -oE '[0-9]+[.][0-9]+[.][0-9]+(-[0-9A-Za-z.-]+)?$' || true)"
  if [ -z "$version" ]; then
    echo "::error::Cannot read a version from '$name.nupkg'."
    exit 1
  fi
  printf '%s\n' "${name%."$version"}" >> "$work/packed.txt"
  printf '%s\n' "$version" >> "$work/versions.txt"
done
shopt -u nullglob
sort -o "$work/packed.txt" "$work/packed.txt"
sort -u -o "$work/versions.txt" "$work/versions.txt"
packed_count=$(wc -l < "$work/packed.txt")

failed=0
report() {
  echo "::error::$1"
  sed 's/^/  /' "$2"
  failed=1
}

comm -23 "$work/expected.txt" "$work/packed.txt" > "$work/missing.txt"
comm -13 "$work/expected.txt" "$work/packed.txt" > "$work/unexpected.txt"
uniq -d "$work/packed.txt" > "$work/duplicate.txt"
[ -s "$work/missing.txt" ] && report "$(wc -l < "$work/missing.txt") expected package(s) missing from $package_dir:" "$work/missing.txt"
[ -s "$work/unexpected.txt" ] && report "$(wc -l < "$work/unexpected.txt") package(s) in $package_dir that no packable project produces (stale file, or a project that is no longer packable):" "$work/unexpected.txt"
[ -s "$work/duplicate.txt" ] && report "Package(s) present at more than one version:" "$work/duplicate.txt"

# 3. One version for everything.
version_count=$(wc -l < "$work/versions.txt")
version="$(head -n 1 "$work/versions.txt")"
if [ "$version_count" -ne 1 ]; then
  report "Version lockstep broken: $version_count distinct versions." "$work/versions.txt"
elif [[ "$version" == 0.0.0* ]]; then
  echo "::error::MinVer stamped $version: the checkout has no tag history (actions/checkout needs fetch-depth: 0)."
  failed=1
elif [ -n "$expected_version" ] && [ "$version" != "$expected_version" ]; then
  echo "::error::Packed version $version does not match the expected version $expected_version."
  failed=1
fi

# 4. Directory.Packages.props pins exactly the shipped set, so a consumer-verify harness or sample
#    can reference any package, and a renamed or deleted package leaves no stale pin behind.
grep -oE '<PackageVersion Include="SharedKernel\.[^"]+"' "$repo_root/Directory.Packages.props" \
  | sed -E 's/.*Include="([^"]+)"/\1/' | sort > "$work/pinned.txt"
comm -23 "$work/expected.txt" "$work/pinned.txt" > "$work/unpinned.txt"
comm -13 "$work/expected.txt" "$work/pinned.txt" > "$work/stale-pins.txt"
[ -s "$work/unpinned.txt" ] && report "Directory.Packages.props has no SharedKernel <PackageVersion> for:" "$work/unpinned.txt"
[ -s "$work/stale-pins.txt" ] && report "Directory.Packages.props pins SharedKernel package(s) that no project packs:" "$work/stale-pins.txt"

if [ "$failed" -ne 0 ]; then
  echo "Package set verification FAILED ($packed_count packed, $expected_count expected)."
  exit 1
fi

echo "OK: all $expected_count expected packages are present in $package_dir at one version, $version."
if [ -n "${WRITE_EXPECTED_TO:-}" ]; then
  cp "$work/expected.txt" "$WRITE_EXPECTED_TO"
fi
if [ -n "${GITHUB_ENV:-}" ]; then
  echo "SK_VERSION=$version" >> "$GITHUB_ENV"
fi
