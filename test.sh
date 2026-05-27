#!/usr/bin/env bash
set -euo pipefail
dotnet test CodeEdit.sln --logger "console;verbosity=normal"
