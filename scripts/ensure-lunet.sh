#!/usr/bin/env bash
set -euo pipefail

lunet_version="${LUNET_VERSION:-1.0.10}"
lunet_framework="${LUNET_FRAMEWORK:-net10.0}"
download_url="https://www.nuget.org/api/v2/package/lunet/${lunet_version}"

global_packages_dir="$(dotnet nuget locals global-packages --list | sed -e 's/^global-packages: //' -e 's/\r$//')"
if [[ -z "$global_packages_dir" ]]; then
  echo "Unable to resolve the global NuGet packages directory." >&2
  exit 1
fi

package_root="${global_packages_dir%/}/lunet/${lunet_version}"
lunet_dll="${package_root}/tools/${lunet_framework}/any/lunet.dll"

if [[ -f "$lunet_dll" ]]; then
  printf '%s\n' "$lunet_dll"
  exit 0
fi

if ! command -v unzip >/dev/null 2>&1; then
  echo "unzip is required to bootstrap Lunet from NuGet." >&2
  exit 1
fi

bootstrap_dir="$(mktemp -d)"
cleanup() {
  rm -rf "$bootstrap_dir"
}
trap cleanup EXIT

package_path="${bootstrap_dir}/lunet.${lunet_version}.nupkg"

if command -v curl >/dev/null 2>&1; then
  curl -fsSL "$download_url" -o "$package_path"
elif command -v wget >/dev/null 2>&1; then
  wget -qO "$package_path" "$download_url"
else
  echo "curl or wget is required to bootstrap Lunet from NuGet." >&2
  exit 1
fi

mkdir -p "$package_root"
unzip -oq "$package_path" -d "$package_root"

if [[ ! -f "$lunet_dll" ]]; then
  echo "Failed to bootstrap Lunet runtime at ${lunet_dll}." >&2
  exit 1
fi

printf '%s\n' "$lunet_dll"
