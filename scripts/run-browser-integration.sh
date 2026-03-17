#!/usr/bin/env bash
set -euo pipefail

configuration="Release"
python_bin="python3"
port="${NATIVEWEBVIEW_BROWSER_INTEGRATION_PORT:-8123}"
no_browser_install=false
no_build=false
publish_dir="artifacts/integration/browser/publish"
server_log="artifacts/integration/browser/server.log"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --configuration)
      configuration="$2"
      shift 2
      ;;
    --python)
      python_bin="$2"
      shift 2
      ;;
    --port)
      port="$2"
      shift 2
      ;;
    --no-browser-install)
      no_browser_install=true
      shift
      ;;
    --no-build)
      no_build=true
      shift
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 1
      ;;
  esac
done

mkdir -p "$(dirname "$publish_dir")"

if ! dotnet workload list 2>/dev/null | grep -q 'wasm-tools'; then
  echo "The browser integration runner requires the 'wasm-tools' workload." >&2
  echo "Install it with: dotnet workload install wasm-tools" >&2
  exit 1
fi

if [[ "$no_build" != "true" ]]; then
  dotnet publish tests/NativeWebView.Integration.Browser/NativeWebView.Integration.Browser.csproj \
    -c "$configuration" \
    -o "$publish_dir"
fi

if [[ ! -d "$publish_dir" ]]; then
  echo "Browser publish directory was not found: $publish_dir" >&2
  exit 1
fi

serve_dir=""
if [[ -f "$publish_dir/index.html" && -d "$publish_dir/_framework" ]]; then
  serve_dir="$publish_dir"
else
  while IFS= read -r candidate; do
    candidate_dir="$(dirname "$candidate")"
    if [[ -d "$candidate_dir/_framework" ]]; then
      serve_dir="$candidate_dir"
      break
    fi
  done < <(find "$publish_dir" -type f -name index.html | sort)
fi

if [[ -z "$serve_dir" ]]; then
  echo "Unable to locate a browser publish output with index.html and _framework." >&2
  exit 1
fi

if [[ -f "$serve_dir/avalonia.js" && ! -f "$serve_dir/_framework/avalonia.js" ]]; then
  cp "$serve_dir/avalonia.js" "$serve_dir/_framework/avalonia.js"
fi

if [[ -f "$serve_dir/avalonia.js.map" && ! -f "$serve_dir/_framework/avalonia.js.map" ]]; then
  cp "$serve_dir/avalonia.js.map" "$serve_dir/_framework/avalonia.js.map"
fi

if ! command -v "$python_bin" >/dev/null 2>&1 && [[ ! -x "$python_bin" ]]; then
  echo "Python runtime not found: $python_bin" >&2
  exit 1
fi

npm ci --prefix tests/NativeWebView.Playwright
if [[ "$no_browser_install" != "true" ]]; then
  if [[ "$(uname -s)" == "Linux" ]]; then
    npx --prefix tests/NativeWebView.Playwright playwright install --with-deps chromium
  else
    npx --prefix tests/NativeWebView.Playwright playwright install chromium
  fi
fi

mkdir -p "$(dirname "$server_log")"
"$python_bin" -m http.server "$port" --directory "$serve_dir" > "$server_log" 2>&1 &
server_pid=$!

cleanup() {
  if [[ -n "${server_pid:-}" ]]; then
    kill "$server_pid" >/dev/null 2>&1 || true
  fi
}
trap cleanup EXIT

base_url="http://127.0.0.1:${port}"

for _ in $(seq 1 30); do
  if curl -fsS "${base_url}/index.html" >/dev/null 2>&1; then
    break
  fi

  sleep 1
done

if ! curl -fsS "${base_url}/index.html" >/dev/null 2>&1; then
  echo "Browser integration server did not become ready at ${base_url}." >&2
  exit 1
fi

(
  cd tests/NativeWebView.Playwright
  NATIVEWEBVIEW_PLAYWRIGHT_MODE="browser-integration" \
  NATIVEWEBVIEW_BROWSER_INTEGRATION_URL="${base_url}/index.html" \
  npx playwright test specs/browser-integration.spec.mjs
)

echo "Browser integration passed."
