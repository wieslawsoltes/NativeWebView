#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LOCK_DIR="${SCRIPT_DIR}/site/.lunet/.build-lock"

search_logs() {
    local pattern="$1"
    shift

    if command -v rg >/dev/null 2>&1; then
        rg -n -e "$pattern" "$@"
    else
        grep -n -E "$pattern" "$@"
    fi
}

clean_docs_outputs() {
    find "${SCRIPT_DIR}/src" -path '*/obj/Release/*/NativeWebView*.api.json' -delete
    rm -rf "${SCRIPT_DIR}/site/.lunet/build/cache/api/dotnet" \
           "${SCRIPT_DIR}/site/.lunet/build/www"
}

cd "${SCRIPT_DIR}"
while ! mkdir "${LOCK_DIR}" 2>/dev/null; do
    sleep 1
done

LUNET_LOG=""
cleanup() {
    if [ -n "${LUNET_LOG}" ] && [ -f "${LUNET_LOG}" ]; then
        rm -f "${LUNET_LOG}"
    fi

    rmdir "${LOCK_DIR}" 2>/dev/null || true
}

trap cleanup EXIT

LUNET_DLL="$("${SCRIPT_DIR}/scripts/ensure-lunet.sh")"
clean_docs_outputs

cd site
LUNET_LOG="$(mktemp)"

set +e
dotnet "${LUNET_DLL}" --stacktrace build 2>&1 | tee "${LUNET_LOG}"
lunet_exit_code=${PIPESTATUS[0]}
set -e

if search_logs 'Error while building api dotnet|Lunet\.Api\.DotNet\.DotNetProgramException|Unable to select the api dotnet output' "${LUNET_LOG}" >/dev/null; then
    echo "Lunet reported API/site build errors."
    exit 1
fi

if [ "${lunet_exit_code}" -ne 0 ] &&
   ! search_logs 'Unable to build api dotnet' "${LUNET_LOG}" >/dev/null; then
    echo "Lunet build failed with exit code ${lunet_exit_code}."
    exit "${lunet_exit_code}"
fi
