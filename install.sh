#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
INSTALL_DIR="${HOME}/.local/bin"
BINARY_NAME="ce"
PROJECT="${SCRIPT_DIR}/src/CodeEdit.Presentation"
PUBLISH_DIR="${SCRIPT_DIR}/publish/linux-x64"

echo "Building ${BINARY_NAME}…"
dotnet publish "${PROJECT}" \
  --configuration Release \
  --runtime linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:DebugType=none \
  --output "${PUBLISH_DIR}"

mkdir -p "${INSTALL_DIR}"
cp "${PUBLISH_DIR}/CodeEdit.Presentation" "${INSTALL_DIR}/${BINARY_NAME}"
chmod +x "${INSTALL_DIR}/${BINARY_NAME}"

echo "Installed: ${INSTALL_DIR}/${BINARY_NAME}"
