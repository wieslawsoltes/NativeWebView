#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_NAME="NativeWebView"
LEGACY_REPO_NAME="${REPO_NAME/View/VIew}"
LEGACY_GITHUB_REPO_URL="https://github.com/wieslawsoltes/${LEGACY_REPO_NAME}"
LEGACY_PAGES_URL="https://wieslawsoltes.github.io/${LEGACY_REPO_NAME}"
LEGACY_BASEPATH_REGEX="(href|src)=\"/${LEGACY_REPO_NAME}/"

search_generated_regex() {
    local pattern="$1"
    shift

    if command -v rg >/dev/null 2>&1; then
        rg -n -g '*.html' -e "$pattern" "$@"
    else
        grep -R -n -E --include='*.html' "$pattern" "$@"
    fi
}

search_generated_fixed() {
    local text="$1"
    shift

    if command -v rg >/dev/null 2>&1; then
        rg -n -F "$text" "$@"
    else
        grep -R -n -F "$text" "$@"
    fi
}

"${SCRIPT_DIR}/build-docs.sh"

DOC_ROOT="${SCRIPT_DIR}/site/.lunet/build/www"

test -f "${DOC_ROOT}/index.html"
test -f "${DOC_ROOT}/api/index.html"
test -f "${DOC_ROOT}/articles/index.html"
test -f "${DOC_ROOT}/articles/getting-started/index.html"
test -f "${DOC_ROOT}/articles/controls/index.html"
test -f "${DOC_ROOT}/articles/rendering/index.html"
test -f "${DOC_ROOT}/articles/platforms/index.html"
test -f "${DOC_ROOT}/articles/diagnostics/index.html"
test -f "${DOC_ROOT}/articles/reference/index.html"
test -f "${DOC_ROOT}/articles/getting-started/overview/index.html"
test -f "${DOC_ROOT}/articles/getting-started/installation/index.html"
test -f "${DOC_ROOT}/articles/getting-started/quickstart/index.html"
test -f "${DOC_ROOT}/articles/getting-started/sample-feature-explorer/index.html"
test -f "${DOC_ROOT}/articles/controls/nativewebview/index.html"
test -f "${DOC_ROOT}/articles/controls/nativewebdialog/index.html"
test -f "${DOC_ROOT}/articles/controls/webauthenticationbroker/index.html"
test -f "${DOC_ROOT}/articles/rendering/render-modes/index.html"
test -f "${DOC_ROOT}/articles/platforms/windows/index.html"
test -f "${DOC_ROOT}/articles/platforms/browser/index.html"
test -f "${DOC_ROOT}/articles/diagnostics/platform-prerequisites/index.html"
test -f "${DOC_ROOT}/articles/reference/platform-support-matrix/index.html"
test -f "${DOC_ROOT}/articles/reference/package-layout/index.html"
test -f "${DOC_ROOT}/articles/reference/lunet-docs-pipeline/index.html"
test -f "${DOC_ROOT}/articles/reference/license/index.html"
test -f "${DOC_ROOT}/css/lite.css"

if search_generated_regex 'href="[^"]*\.md([?#"][^"]*)?"' "${DOC_ROOT}" | grep -vE 'href="https?://' >/dev/null; then
    echo "Generated docs contain raw .md links."
    exit 1
fi

if search_generated_regex 'href="[^"]*/readme([?#"][^"]*)?"' "${DOC_ROOT}" >/dev/null; then
    echo "Generated docs contain /readme routes instead of directory routes."
    exit 1
fi

if search_generated_regex 'href="[^"]*/api/index\.md([?#"][^"]*)?"' "${DOC_ROOT}" >/dev/null; then
    echo "Generated docs contain stale /api/index.md links."
    exit 1
fi

if find "${DOC_ROOT}/articles" -name '*.md' -print -quit | grep -q .; then
    echo "Generated docs still contain raw .md article outputs."
    find "${DOC_ROOT}/articles" -name '*.md' -print
    exit 1
fi

if search_generated_fixed "${LEGACY_GITHUB_REPO_URL}" "${DOC_ROOT}/index.html" "${DOC_ROOT}/articles" >/dev/null; then
    echo "Generated docs still contain the legacy GitHub repository URL."
    exit 1
fi

if search_generated_fixed "${LEGACY_PAGES_URL}" "${DOC_ROOT}/index.html" "${DOC_ROOT}/articles" >/dev/null; then
    echo "Generated docs still contain the legacy GitHub Pages URL."
    exit 1
fi

if search_generated_regex "${LEGACY_BASEPATH_REGEX}" "${DOC_ROOT}" >/dev/null; then
    echo "Generated docs still contain the legacy production base path."
    exit 1
fi

if search_generated_regex 'Creative Commons|CC BY 2.5' "${DOC_ROOT}/index.html" "${DOC_ROOT}/articles/getting-started/overview/index.html" >/dev/null; then
    echo "Generated docs contain the default Creative Commons footer instead of the project MIT license footer."
    exit 1
fi

if ! search_generated_fixed 'MIT license' "${DOC_ROOT}/index.html" >/dev/null; then
    echo "Generated site footer is missing the project MIT license text."
    exit 1
fi

NATIVE_WEBVIEW_API_PAGE="${DOC_ROOT}/api/NativeWebView.Controls.NativeWebView/index.html"
if test -f "${NATIVE_WEBVIEW_API_PAGE}"; then
    if ! search_generated_fixed 'https://api-docs.avaloniaui.net/docs/Avalonia.Controls.Control/' "${NATIVE_WEBVIEW_API_PAGE}" >/dev/null; then
        echo "Generated NativeWebView API page is missing the external Avalonia.Controls.Control link."
        exit 1
    fi
else
    echo "Warning: Lunet did not emit per-type API pages on this host; skipping NativeWebView API page validation."
fi

GETTING_STARTED_INDEX_PAGE="${DOC_ROOT}/articles/getting-started/index.html"
if ! search_generated_fixed "/${REPO_NAME}/css/lite.css" "${GETTING_STARTED_INDEX_PAGE}" >/dev/null; then
    echo "Production getting-started page is missing the project-basepath-prefixed lite.css URL."
    exit 1
fi
