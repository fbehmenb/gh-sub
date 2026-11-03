#!/usr/bin/env bash
set -euo pipefail

tag="${1:?tag fehlt}"
root="$(cd "$(dirname "$0")/.." && pwd)"
stage="${RUNNER_TEMP:-/tmp}/gh-sub-publish"
rm -rf "$stage"
mkdir -p "$stage" "$root/dist"

dotnet publish "$root/UmbrellaManager/UmbrellaManager.csproj" \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:DebugType=none \
  -p:DebugSymbols=false \
  -o "$stage"

mv "$stage/umbrella-manager-local.exe" "$root/dist/gh-sub_${tag}_windows-amd64.exe"
