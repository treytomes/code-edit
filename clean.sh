#!/usr/bin/env bash
set -euo pipefail
find . -type d \( -name bin -o -name obj \) -not -path './.git/*' -exec rm -rf {} +
echo "Cleaned."
