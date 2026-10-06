#!/usr/bin/env bash
# Builds the Shop against the kernel packed from this checkout.
#
#   samples/Shop/build.sh             pack the kernel, build the Shop
#   samples/Shop/build.sh --no-pack   reuse the packages already in nupkgs/
#   samples/Shop/build.sh --test      also run the unit tests (no Docker)
#   samples/Shop/build.sh --e2e       also run the end-to-end flows (Docker; minutes)
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
pack=1 test=0 e2e=0
for arg in "$@"; do
  case "$arg" in
    --no-pack) pack=0 ;;
    --test) test=1 ;;
    --e2e) test=1 e2e=1 ;;
    *) echo "unknown option: $arg" >&2; exit 2 ;;
  esac
done

cd "$root"
if [ "$pack" = 1 ]; then
  rm -f nupkgs/SharedKernel.*.nupkg nupkgs/SharedKernel.*.snupkg
  dotnet pack Platform.SharedKernel.slnx -c Release -nologo -v:q
fi

primitives=$(ls nupkgs/SharedKernel.Primitives.*.nupkg 2>/dev/null | head -1)
[ -n "$primitives" ] || { echo "nupkgs/ holds no SharedKernel packages; run without --no-pack" >&2; exit 1; }
version=$(basename "$primitives" .nupkg); version=${version#SharedKernel.Primitives.}
echo "Shop builds against SharedKernel $version"

# A repack at the same commit reuses the version number; drop that version from the NuGet cache so the new bits win.
cache=$(dotnet nuget locals global-packages --list | sed 's/^[^:]*: //')
for dir in "$cache"/sharedkernel.*/"$version"; do [ -d "$dir" ] && rm -rf "$dir"; done

dotnet build samples/Shop/Shop.slnx -nologo -p:SharedKernelPackageVersion="$version"

if [ "$test" = 1 ]; then
  dotnet test samples/Shop/Shop.slnx --no-build -nologo -p:SharedKernelPackageVersion="$version"
fi
if [ "$e2e" = 1 ]; then
  SHOP_E2E=1 dotnet test samples/Shop/Shop.E2E --no-build -nologo -p:SharedKernelPackageVersion="$version"
fi
