#!/usr/bin/env bash
set -euo pipefail

package_dir="artifacts/packages"
version=""

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
    *)
      echo "Unknown argument: $1" >&2
      exit 1
      ;;
  esac
done

if [[ -z "$version" ]]; then
  package_path="$(find "$package_dir" -maxdepth 1 -type f -name 'NativeWebView.Platform.Windows.*.nupkg' ! -name '*.snupkg' | sort | tail -n 1)"
  if [[ -z "$package_path" ]]; then
    echo "NativeWebView.Platform.Windows package was not found in $package_dir." >&2
    exit 1
  fi

  package_name="$(basename "$package_path" .nupkg)"
  version="${package_name#NativeWebView.Platform.Windows.}"
fi

package_dir="$(cd "$package_dir" && pwd)"
package_source="$package_dir"
if command -v cygpath >/dev/null 2>&1; then
  package_source="$(cygpath -m "$package_dir")"
fi
work_dir="$(mktemp -d)"
trap 'rm -rf "$work_dir"' EXIT

cat > "$work_dir/NuGet.Config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="NativeWebView packages" value="$package_source" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF

cat > "$work_dir/PackageConsumer.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFrameworks>net10.0;net11.0</TargetFrameworks>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="NativeWebView" Version="$version" />
    <PackageReference Include="NativeWebView.Platform.Windows" Version="$version" />
  </ItemGroup>
</Project>
EOF

cat > "$work_dir/Program.cs" <<'EOF'
Console.WriteLine(typeof(NativeWebView.Controls.NativeWebView).Assembly.FullName);
EOF

for framework in net10.0 net11.0; do
  dotnet publish "$work_dir/PackageConsumer.csproj" \
    --disable-build-servers \
    --framework "$framework" \
    --configuration Release \
    --configfile "$work_dir/NuGet.Config" \
    --output "$work_dir/publish/$framework" \
    --nologo

  required_files=(
    "NativeWebView.dll"
    "NativeWebView.Platform.Windows.dll"
    "Microsoft.Web.WebView2.Core.dll"
    "WebView2Loader.dll"
  )

  for required_file in "${required_files[@]}"; do
    if ! find "$work_dir/publish/$framework" -type f -name "$required_file" -print -quit | grep -q .; then
      echo "Published Windows consumer is missing $required_file." >&2
      exit 1
    fi
  done

  deps_file="$work_dir/publish/$framework/PackageConsumer.deps.json"
  if [[ ! -f "$deps_file" ]] || ! grep -q 'Microsoft.Web.WebView2.Core.dll' "$deps_file"; then
    echo "Published Windows consumer dependency manifest does not include Microsoft.Web.WebView2.Core.dll." >&2
    exit 1
  fi
done

echo "Windows package consumer validation passed for version ${version}."
