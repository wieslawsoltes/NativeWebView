#!/usr/bin/env bash
set -euo pipefail

python_bin="python3"
venv_dir=""
install_browsers=true

while [[ $# -gt 0 ]]; do
  case "$1" in
    --python)
      python_bin="$2"
      shift 2
      ;;
    --venv)
      venv_dir="$2"
      shift 2
      ;;
    --no-browser-install)
      install_browsers=false
      shift
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 1
      ;;
  esac
done

if [[ -n "$venv_dir" && "$python_bin" == "python3" ]]; then
  candidate_python="$venv_dir/bin/python"
  if [[ -x "$candidate_python" ]]; then
    python_bin="$candidate_python"
  fi
fi

if ! command -v "$python_bin" >/dev/null 2>&1 && [[ ! -x "$python_bin" ]]; then
  echo "Python runtime not found: $python_bin" >&2
  exit 1
fi

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
lunet_dll="$("${repo_root}/scripts/ensure-lunet.sh")"
(
  cd "${repo_root}/site"
  dotnet "${lunet_dll}" --stacktrace build --dev
)

export NATIVEWEBVIEW_DOCS_PYTHON_BIN="$python_bin"

npm ci --prefix "${repo_root}/tests/NativeWebView.Playwright"
if [[ "$install_browsers" == "true" ]]; then
  if [[ "$(uname -s)" == "Linux" ]]; then
    npx --prefix "${repo_root}/tests/NativeWebView.Playwright" playwright install --with-deps chromium
  else
    npx --prefix "${repo_root}/tests/NativeWebView.Playwright" playwright install chromium
  fi
fi
npm --prefix "${repo_root}/tests/NativeWebView.Playwright" test
