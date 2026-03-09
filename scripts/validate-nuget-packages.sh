#!/usr/bin/env bash
set -euo pipefail

package_dir="artifacts/packages"
version=""
markdown_output=""

packages=(
  "NativeWebView"
  "NativeWebView.Core"
  "NativeWebView.Dialog"
  "NativeWebView.Auth"
  "NativeWebView.Interop"
  "NativeWebView.Platform.Windows"
  "NativeWebView.Platform.macOS"
  "NativeWebView.Platform.Linux"
  "NativeWebView.Platform.iOS"
  "NativeWebView.Platform.Android"
  "NativeWebView.Platform.Browser"
)

package_match_order=(
  "NativeWebView.Platform.Windows"
  "NativeWebView.Platform.Android"
  "NativeWebView.Platform.Browser"
  "NativeWebView.Platform.Linux"
  "NativeWebView.Platform.macOS"
  "NativeWebView.Platform.iOS"
  "NativeWebView.Interop"
  "NativeWebView.Dialog"
  "NativeWebView.Core"
  "NativeWebView.Auth"
  "NativeWebView"
)

collect_distinct_versions() {
  local package_path
  local package_name
  local package_id
  local version_candidate
  local -a discovered_versions=()

  while IFS= read -r package_path; do
    package_name="$(basename "$package_path" .nupkg)"
    for package_id in "${package_match_order[@]}"; do
      version_candidate="${package_name#${package_id}.}"
      if [[ "$version_candidate" != "$package_name" ]]; then
        discovered_versions+=("$version_candidate")
        break
      fi
    done
  done < <(find "$package_dir" -maxdepth 1 -type f -name '*.nupkg' ! -name '*.snupkg' | sort)

  if [[ ${#discovered_versions[@]} -eq 0 ]]; then
    return 0
  fi

  printf '%s\n' "${discovered_versions[@]}" | sort -u
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --package-dir)
      package_dir="$2"
      shift 2
      ;;
    --version)
      version="$2"
      shift 2
      ;;
    --markdown-output)
      markdown_output="$2"
      shift 2
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 1
      ;;
  esac
done

if [[ ! -d "$package_dir" ]]; then
  echo "Package directory was not found: $package_dir" >&2
  exit 1
fi

if [[ -z "$version" ]]; then
  discovered_versions_raw="$(collect_distinct_versions || true)"
  discovered_versions=()
  while IFS= read -r discovered_version; do
    if [[ -n "$discovered_version" ]]; then
      discovered_versions+=("$discovered_version")
    fi
  done <<< "$discovered_versions_raw"

  if [[ ${#discovered_versions[@]} -eq 0 ]]; then
    echo "No NuGet packages were found in $package_dir." >&2
    exit 1
  fi

  if [[ ${#discovered_versions[@]} -ne 1 ]]; then
    printf 'Multiple package versions were found in %s. Pass --version explicitly or clean the directory.\n' "$package_dir" >&2
    printf 'Discovered versions:\n' >&2
    printf ' - %s\n' "${discovered_versions[@]}" >&2
    exit 1
  fi

  version="${discovered_versions[0]}"
fi

if [[ -n "$markdown_output" ]]; then
  mkdir -p "$(dirname "$markdown_output")"
fi

dependency_checks=(
  "NativeWebView:NativeWebView.Core NativeWebView.Interop Avalonia"
  "NativeWebView.Dialog:NativeWebView.Core NativeWebView.Interop"
  "NativeWebView.Auth:NativeWebView.Core"
  "NativeWebView.Interop:NativeWebView.Core"
  "NativeWebView.Platform.Windows:NativeWebView.Core NativeWebView.Interop"
  "NativeWebView.Platform.macOS:NativeWebView.Core NativeWebView.Interop"
  "NativeWebView.Platform.Linux:NativeWebView.Core NativeWebView.Interop"
  "NativeWebView.Platform.iOS:NativeWebView.Core NativeWebView.Interop"
  "NativeWebView.Platform.Android:NativeWebView.Core NativeWebView.Interop"
  "NativeWebView.Platform.Browser:NativeWebView.Core NativeWebView.Interop"
)

summary_rows=()
failures=()

find_dependency_spec() {
  local package_id="$1"
  local entry
  for entry in "${dependency_checks[@]}"; do
    if [[ "$entry" == "${package_id}:"* ]]; then
      printf '%s\n' "${entry#*:}"
      return 0
    fi
  done

  printf '%s\n' ""
}

record_failure() {
  local package_id="$1"
  local message="$2"
  failures+=("${package_id}: ${message}")
}

for package_id in "${packages[@]}"; do
  package_path="$package_dir/${package_id}.${version}.nupkg"
  symbols_path="$package_dir/${package_id}.${version}.snupkg"
  package_ok="Yes"
  symbols_ok="Yes"
  contents_ok="Yes"
  metadata_ok="Yes"
  dependency_ok="Yes"

  if [[ ! -f "$package_path" ]]; then
    record_failure "$package_id" "Missing package archive: $package_path"
    package_ok="No"
    symbols_ok="No"
    contents_ok="No"
    metadata_ok="No"
    dependency_ok="No"
    summary_rows+=("| \`${package_id}\` | ${package_ok} | ${symbols_ok} | ${contents_ok} | ${metadata_ok} | ${dependency_ok} |")
    continue
  fi

  if [[ ! -f "$symbols_path" ]]; then
    record_failure "$package_id" "Missing symbols package: $symbols_path"
    symbols_ok="No"
  fi

  archive_listing="$(unzip -Z1 "$package_path")"

  if ! grep -qx "README.md" <<< "$archive_listing"; then
    record_failure "$package_id" "README.md was not packed."
    contents_ok="No"
  fi

  if ! grep -qx "LICENSE" <<< "$archive_listing"; then
    record_failure "$package_id" "LICENSE was not packed."
    contents_ok="No"
  fi

  if ! grep -qx "lib/net8.0/${package_id}.dll" <<< "$archive_listing"; then
    record_failure "$package_id" "Managed assembly lib/net8.0/${package_id}.dll was not packed."
    contents_ok="No"
  fi

  if ! nuspec_content="$(unzip -p "$package_path" "${package_id}.nuspec" 2>/dev/null)"; then
    record_failure "$package_id" "Package nuspec is missing or unreadable."
    metadata_ok="No"
    dependency_ok="No"
    summary_rows+=("| \`${package_id}\` | ${package_ok} | ${symbols_ok} | ${contents_ok} | ${metadata_ok} | ${dependency_ok} |")
    continue
  fi

  if ! grep -q "<id>${package_id}</id>" <<< "$nuspec_content"; then
    record_failure "$package_id" "Nuspec id is missing or incorrect."
    metadata_ok="No"
  fi

  if ! grep -q "<version>${version}</version>" <<< "$nuspec_content"; then
    record_failure "$package_id" "Nuspec version does not match ${version}."
    metadata_ok="No"
  fi

  if ! grep -q '<readme>README.md</readme>' <<< "$nuspec_content"; then
    record_failure "$package_id" "Package readme metadata is missing."
    metadata_ok="No"
  fi

  if ! grep -q '<license type="expression">MIT</license>' <<< "$nuspec_content"; then
    record_failure "$package_id" "Package license metadata is missing."
    metadata_ok="No"
  fi

  if ! grep -q '<projectUrl>https://github.com/wieslawsoltes/NativeWebVIew</projectUrl>' <<< "$nuspec_content"; then
    record_failure "$package_id" "Package project URL metadata is missing."
    metadata_ok="No"
  fi

  expected_dependencies="$(find_dependency_spec "$package_id")"
  if [[ -n "$expected_dependencies" ]]; then
    for dependency_id in $expected_dependencies; do
      if ! grep -q "dependency id=\"${dependency_id}\"" <<< "$nuspec_content"; then
        record_failure "$package_id" "Expected dependency ${dependency_id} is missing from nuspec."
        dependency_ok="No"
      fi
    done
  fi

  summary_rows+=("| \`${package_id}\` | ${package_ok} | ${symbols_ok} | ${contents_ok} | ${metadata_ok} | ${dependency_ok} |")
done

if [[ -n "$markdown_output" ]]; then
  {
    echo "# NuGet Package Validation"
    echo
    echo "Version: \`${version}\`"
    echo
    echo "| Package | .nupkg | .snupkg | Contents | Metadata | Dependencies |"
    echo "| --- | --- | --- | --- | --- | --- |"
    printf '%s\n' "${summary_rows[@]}"

    if [[ ${#failures[@]} -gt 0 ]]; then
      echo
      echo "## Failures"
      echo
      for failure in "${failures[@]}"; do
        echo "- ${failure}"
      done
    fi
  } > "$markdown_output"
fi

if [[ ${#failures[@]} -gt 0 ]]; then
  printf 'NuGet package validation failed:\n' >&2
  for failure in "${failures[@]}"; do
    printf ' - %s\n' "$failure" >&2
  done
  exit 1
fi

echo "NuGet package validation passed for version ${version}."
