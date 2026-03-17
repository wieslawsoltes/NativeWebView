#!/usr/bin/env bash
set -euo pipefail

configuration="Release"
no_build=false
skip_simulator_boot=false
device_name="${IOS_SIMULATOR_DEVICE_NAME:-iPhone 15}"
device_type="${IOS_SIMULATOR_DEVICE_TYPE:-com.apple.CoreSimulator.SimDeviceType.iPhone-15}"
runtime_identifier="${IOS_SIMULATOR_RUNTIME_IDENTIFIER:-}"
python_bin="${PYTHON_BIN:-python3}"
bundle_id="com.wieslawsoltes.nativewebview.integration.ios"
project_path="tests/NativeWebView.Integration.iOS/NativeWebView.Integration.iOS.csproj"
framework="net8.0-ios17.0"
timeout_seconds="${IOS_INTEGRATION_TIMEOUT_SECONDS:-240}"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --configuration)
      configuration="$2"
      shift 2
      ;;
    --no-build)
      no_build=true
      shift
      ;;
    --skip-simulator-boot)
      skip_simulator_boot=true
      shift
      ;;
    --device-name)
      device_name="$2"
      shift 2
      ;;
    --device-type)
      device_type="$2"
      shift 2
      ;;
    --runtime)
      runtime_identifier="$2"
      shift 2
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 1
      ;;
  esac
done

created_device=0
sim_udid=""

cleanup() {
  if [[ -n "$sim_udid" ]]; then
    xcrun simctl terminate "$sim_udid" "$bundle_id" >/dev/null 2>&1 || true
  fi

  if [[ "$skip_simulator_boot" == "true" ]]; then
    return
  fi

  if [[ -n "$sim_udid" ]]; then
    xcrun simctl shutdown "$sim_udid" >/dev/null 2>&1 || true
    if [[ $created_device -eq 1 ]]; then
      xcrun simctl delete "$sim_udid" >/dev/null 2>&1 || true
    fi
  fi
}
trap cleanup EXIT

if [[ "$skip_simulator_boot" != "true" ]]; then
  if ! command -v xcrun >/dev/null 2>&1; then
    echo "xcrun is required for iOS simulator integration." >&2
    exit 1
  fi

  if ! command -v "$python_bin" >/dev/null 2>&1; then
    echo "$python_bin is required for simulator runtime discovery." >&2
    exit 1
  fi

  if [[ -z "$runtime_identifier" ]]; then
    runtime_identifier="$(xcrun simctl list runtimes available -j | "$python_bin" -c 'import json,sys,re
runtimes=json.load(sys.stdin).get("runtimes", [])
ios=[r for r in runtimes if r.get("isAvailable") and "iOS" in r.get("name", "")]
def key(version):
    return tuple(int(part) for part in re.findall(r"\d+", version or ""))
ios.sort(key=lambda r: key(r.get("version", "")), reverse=True)
print(ios[0]["identifier"] if ios else "")')"
  fi

  if [[ -z "$runtime_identifier" ]]; then
    echo "No available iOS simulator runtime found." >&2
    exit 1
  fi

  sim_udid="$(xcrun simctl list devices available -j | "$python_bin" -c 'import json,sys
name=sys.argv[1]
runtime=sys.argv[2]
devices=json.load(sys.stdin).get("devices", {})
for entry in devices.get(runtime, []):
    if entry.get("isAvailable") and entry.get("name")==name:
        print(entry.get("udid", ""))
        raise SystemExit(0)
print("")' "$device_name" "$runtime_identifier")"

  if [[ -z "$sim_udid" ]]; then
    sim_udid="$(xcrun simctl create "$device_name" "$device_type" "$runtime_identifier")"
    created_device=1
  fi

  xcrun simctl boot "$sim_udid" >/dev/null 2>&1 || true
  xcrun simctl bootstatus "$sim_udid" -b
else
  sim_udid="$(xcrun simctl list devices booted -j | "$python_bin" -c 'import json,sys
devices=json.load(sys.stdin).get("devices", {})
for entries in devices.values():
    for entry in entries:
        if entry.get("state") == "Booted":
            print(entry.get("udid", ""))
            raise SystemExit(0)
print("")')"
fi

if [[ -z "$sim_udid" ]]; then
  echo "Unable to resolve a simulator device." >&2
  exit 1
fi

if command -v open >/dev/null 2>&1; then
  # SpringBoard may reject launches until the Simulator app attaches to the booted device.
  open -a Simulator --args -CurrentDeviceUDID "$sim_udid" >/dev/null 2>&1 || true
fi

springboard_deadline=$((SECONDS + 30))
while (( SECONDS < springboard_deadline )); do
  if xcrun simctl launch "$sim_udid" com.apple.Preferences >/dev/null 2>&1; then
    xcrun simctl terminate "$sim_udid" com.apple.Preferences >/dev/null 2>&1 || true
    break
  fi

  sleep 2
done

if ! xcrun simctl launch "$sim_udid" com.apple.Preferences >/dev/null 2>&1; then
  echo "Timed out waiting for SpringBoard to accept application launches on simulator ${sim_udid}." >&2
  exit 1
fi
xcrun simctl terminate "$sim_udid" com.apple.Preferences >/dev/null 2>&1 || true

arch="$(uname -m)"
runtime_id="iossimulator-x64"
if [[ "$arch" == "arm64" ]]; then
  runtime_id="iossimulator-arm64"
fi

if [[ "$no_build" != "true" ]]; then
  dotnet build "$project_path" \
    -c "$configuration" \
    -f "$framework" \
    -p:RuntimeIdentifier="$runtime_id"
fi

app_bundle="$(find "tests/NativeWebView.Integration.iOS/bin/${configuration}" -type d -name 'NativeWebView.Integration.iOS.app' | sort | head -n 1)"
if [[ -z "$app_bundle" ]]; then
  echo "Unable to find built iOS app bundle." >&2
  exit 1
fi

artifacts_dir="$(pwd)/artifacts/integration/ios"
mkdir -p "$artifacts_dir"
stdout_log="${artifacts_dir}/stdout.log"
stderr_log="${artifacts_dir}/stderr.log"
launch_stderr_log="${artifacts_dir}/launch-stderr.log"
: > "$stdout_log"
: > "$stderr_log"
: > "$launch_stderr_log"

xcrun simctl install "$sim_udid" "$app_bundle"
xcrun simctl terminate "$sim_udid" "$bundle_id" >/dev/null 2>&1 || true

launch_deadline=$((SECONDS + 30))
launch_succeeded=false
while (( SECONDS < launch_deadline )); do
  if xcrun simctl launch \
      --terminate-running-process \
      --stdout="$stdout_log" \
      --stderr="$stderr_log" \
      "$sim_udid" \
      "$bundle_id" >/dev/null 2>"$launch_stderr_log"; then
    launch_succeeded=true
    break
  fi

  sleep 2
done

if [[ "$launch_succeeded" != "true" ]]; then
  cat "$launch_stderr_log" >&2
  exit 1
fi

deadline=$((SECONDS + timeout_seconds))
while (( SECONDS < deadline )); do
  result_line="$(grep -h 'NATIVEWEBVIEW_INTEGRATION_RESULT:' "$stdout_log" "$stderr_log" 2>/dev/null | tail -n 1 || true)"
  if [[ -n "$result_line" ]]; then
    printf '%s\n' "$result_line"
    if grep -q '"passed":true}$' <<< "$result_line"; then
      echo "iOS simulator integration passed."
      exit 0
    fi

    echo "iOS simulator integration reported failure." >&2
    exit 1
  fi

  sleep 2
done

echo "Timed out waiting for iOS integration result after ${timeout_seconds}s." >&2
exit 1
