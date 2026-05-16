#!/usr/bin/env bash
# native/build.sh — build libwdsp + libminiaudio and stage them for .NET.
#
# Usage:
#   ./native/build.sh                 # Release, auto-detect RID
#   ./native/build.sh Debug           # pass build type as arg
#   ./native/build.sh Release wdsp    # build only WDSP (skip miniaudio)
#   ./native/build.sh Release miniaudio # build only miniaudio (skip WDSP)
#
# Run from the repo root. Output goes to Zeus.Dsp/runtimes/<rid>/native/ so
# `dotnet publish` / `NativeLibrary.Load("wdsp"|"miniaudio")` picks it up
# automatically (no extra runtime config — same convention for both libs).

set -euo pipefail

BUILD_TYPE="${1:-Release}"
WHICH="${2:-all}"   # all | wdsp | miniaudio
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

# Detect platform + arch -> .NET RID + shared-lib filenames.
case "$(uname -s)" in
    Darwin)
        case "$(uname -m)" in
            arm64)  RID="osx-arm64" ;;
            x86_64) RID="osx-x64"   ;;
            *) echo "Unsupported macOS arch: $(uname -m)" >&2; exit 1 ;;
        esac
        WDSP_LIB="libwdsp.dylib"
        MA_LIB="libminiaudio.dylib"
        ;;
    Linux)
        case "$(uname -m)" in
            aarch64|arm64) RID="linux-arm64" ;;
            x86_64)        RID="linux-x64"   ;;
            *) echo "Unsupported Linux arch: $(uname -m)" >&2; exit 1 ;;
        esac
        WDSP_LIB="libwdsp.so"
        MA_LIB="libminiaudio.so"
        ;;
    *)
        echo "Unsupported host OS: $(uname -s). Use cmake directly on Windows." >&2
        exit 1
        ;;
esac

DEST_DIR="${REPO_ROOT}/Zeus.Dsp/runtimes/${RID}/native"
mkdir -p "${DEST_DIR}"

build_wdsp() {
    local src="${SCRIPT_DIR}/wdsp"
    local build="${SCRIPT_DIR}/build"
    echo "==> [wdsp] Configuring (${BUILD_TYPE}, ${RID})"
    cmake -S "${src}" -B "${build}" -DCMAKE_BUILD_TYPE="${BUILD_TYPE}"
    echo "==> [wdsp] Building"
    cmake --build "${build}" --config "${BUILD_TYPE}" --parallel
    echo "==> [wdsp] Staging ${WDSP_LIB} -> ${DEST_DIR}"
    cp "${build}/${WDSP_LIB}" "${DEST_DIR}/${WDSP_LIB}"
    ls -lh "${DEST_DIR}/${WDSP_LIB}"
}

build_miniaudio() {
    local src="${SCRIPT_DIR}/miniaudio"
    local build="${SCRIPT_DIR}/build-miniaudio"
    echo "==> [miniaudio] Configuring (${BUILD_TYPE}, ${RID})"
    cmake -S "${src}" -B "${build}" -DCMAKE_BUILD_TYPE="${BUILD_TYPE}"
    echo "==> [miniaudio] Building"
    cmake --build "${build}" --config "${BUILD_TYPE}" --parallel
    echo "==> [miniaudio] Staging ${MA_LIB} -> ${DEST_DIR}"
    cp "${build}/${MA_LIB}" "${DEST_DIR}/${MA_LIB}"
    ls -lh "${DEST_DIR}/${MA_LIB}"
}

case "${WHICH}" in
    all)
        build_wdsp
        build_miniaudio
        ;;
    wdsp)
        build_wdsp
        ;;
    miniaudio|ma)
        build_miniaudio
        ;;
    *)
        echo "Unknown target '${WHICH}'. Use: all | wdsp | miniaudio" >&2
        exit 1
        ;;
esac

echo "==> Done."
