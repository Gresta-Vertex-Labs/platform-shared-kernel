#!/usr/bin/env bash
# Checks that every packable SharedKernel package is on the GitHub Packages feed and is PUBLIC (P-637).
# A package created by the first publish of a new id starts PRIVATE, and a consuming service's CI
# reads the feed with its own job token, which sees public packages only; one private package breaks
# every service that restores it. GitHub has no API to change visibility, only to read it, so this
# is a check plus a manual remedy. Used by .github/workflows/release.yml (after publish, at the
# released version) and .github/workflows/feed-health.yml (scheduled, at the newest release), and
# runnable locally:
#
#   eng/verify-feed-visibility.sh [version]
#
#   version   with: every package must have this exact version on the feed (the release check).
#             without: every package must exist on the feed with at least one version.
#
# Environment:
#   GH_TOKEN               required. A token that can read the owner's packages: in Actions the job
#                          token with `permissions: packages: read` (for packages linked to the
#                          repository, which a publish from release.yml does), locally a `gh` login
#                          with the read:packages scope.
#   PACKAGE_OWNER          the organization that owns the feed. Default: GITHUB_REPOSITORY_OWNER, else
#                          the owner of the origin remote.
#   EXPECTED_PACKAGES_FILE a list of package ids (release.yml uses the one the verified run produced);
#                          default: eng/PackageInventory.proj, which asks every csproj whether it packs.
#   VISIBILITY_EXTRA_PACKAGES  space-separated ids judged like shipped packages; a way to show locally
#                          that a missing package fails the check. CI never sets it.
#   GH_BIN                 the gh executable (default: gh); a stub replaces it to simulate responses.
#
# The REST call is GET /orgs/{org}/packages/nuget/{name} (.visibility) plus .../versions. A token
# without access to a package gets 404 (not 403), so a package reported missing is "missing or not
# readable by this token"; the message says so, and the remedy lists both causes.
set -euo pipefail

version="${1:-}"
version="${version#v}"
gh_bin="${GH_BIN:-gh}"

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

: "${GH_TOKEN:?GH_TOKEN must be set (a token that can read the owner\'s packages)}"
export GH_TOKEN

owner="${PACKAGE_OWNER:-${GITHUB_REPOSITORY_OWNER:-}}"
if [ -z "$owner" ]; then
  owner="$(git -C "$repo_root" remote get-url origin | sed -E 's#^.*[:/]([^/]+)/[^/]+(\.git)?$#\1#')"
fi
if [ -z "$owner" ]; then
  echo "::error::Cannot determine the package owner; set PACKAGE_OWNER."
  exit 1
fi

native() { if command -v cygpath > /dev/null 2>&1; then cygpath -m "$1"; else printf '%s' "$1"; fi; }

# 1. The packable set.
if [ -n "${EXPECTED_PACKAGES_FILE:-}" ]; then
  tr -d '\r' < "$EXPECTED_PACKAGES_FILE" | sed '/^[[:space:]]*$/d' | sort -u > "$work/packages.txt"
else
  dotnet msbuild "$repo_root/eng/PackageInventory.proj" -nologo -v:q -p:InventoryFile="$(native "$work")/inventory.txt"
  tr -d '\r' < "$work/inventory.txt" | sed '/^[[:space:]]*$/d' | sort -u > "$work/packages.txt"
fi
for extra in ${VISIBILITY_EXTRA_PACKAGES:-}; do echo "$extra" >> "$work/packages.txt"; done
package_count=$(wc -l < "$work/packages.txt")
if [ "$package_count" -eq 0 ]; then
  echo "::error::The packable package set is empty."
  exit 1
fi

# One read of the REST API; prints the jq result, or nothing on a non-2xx answer (gh prints the error
# body on stdout, so the exit status decides).
api() { # <path> <jq filter>
  local out
  if out="$(MSYS_NO_PATHCONV=1 "$gh_bin" api "$1" --jq "$2" 2> /dev/null)"; then printf '%s' "$out"; fi
}

if [ -n "$version" ]; then
  echo "Checking $package_count packages of '$owner' for version $version and public visibility..."
else
  echo "Checking $package_count packages of '$owner' for existence and public visibility..."
fi

: > "$work/problems.txt"
while IFS= read -r id; do
  info="$(api "orgs/$owner/packages/nuget/$id" '[.visibility, (.version_count|tostring)] | join(" ")')"
  if [ -z "$info" ]; then
    echo "$id: not found on the feed (missing, or not readable by this token)" >> "$work/problems.txt"
    continue
  fi
  visibility="${info%% *}"
  if [ "$visibility" != "public" ]; then
    echo "$id: visibility is '$visibility', not public" >> "$work/problems.txt"
  fi
  if [ -n "$version" ]; then
    # Right after a publish the version can take a moment to list; retry before calling it missing.
    found=""
    for attempt in 1 2 3; do
      found="$(api "orgs/$owner/packages/nuget/$id/versions?per_page=100" ".[] | select(.name == \"$version\") | .name")"
      [ -n "$found" ] && break
      [ "$attempt" -lt 3 ] && sleep 10
    done
    [ -n "$found" ] || echo "$id: version $version is not on the feed" >> "$work/problems.txt"
  fi
done < "$work/packages.txt"

if [ -s "$work/problems.txt" ]; then
  count=$(wc -l < "$work/problems.txt")
  echo "::error::$count package problem(s) on the GitHub Packages feed of '$owner':"
  sed 's/^/  /' "$work/problems.txt"
  cat <<EOF
::error::Remedy. A private package: open the package on GitHub (Packages of '$owner' > the package > Package settings > Change visibility > Public) and make it public; GitHub offers no API for this. A missing package: check the publish job pushed it, or that the package is linked to this repository (Package settings > Manage Actions access) so the job token can read it. A release is not complete until this check passes.
EOF
  exit 1
fi

echo "OK: all $package_count packages are on the feed${version:+ at $version} and public."
