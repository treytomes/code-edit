#!/usr/bin/env bash
# Generate an HTML coverage report and open it.
# Usage: ./scripts/coverage.sh
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
RESULTS="$ROOT/coverage/results"
REPORT="$ROOT/coverage/report"

rm -rf "$RESULTS" "$REPORT"

dotnet test "$ROOT/CodeEdit.sln" \
  --settings "$ROOT/coverage.runsettings" \
  --results-directory "$RESULTS" \
  --no-restore

dotnet tool run reportgenerator \
  -reports:"$RESULTS/**/coverage.cobertura.xml" \
  -targetdir:"$REPORT" \
  -reporttypes:"Html;TextSummary;Badges" \
  -title:"code-edit"

echo ""
cat "$REPORT/Summary.txt"
echo ""
echo "Full report: $REPORT/index.html"

# Open in browser if possible
if command -v xdg-open &>/dev/null; then
  xdg-open "$REPORT/index.html"
elif command -v open &>/dev/null; then
  open "$REPORT/index.html"
fi
