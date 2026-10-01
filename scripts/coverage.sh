#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."

results=artifacts/test-results
report=artifacts/coverage
rm -rf "$results" "$report" web/coverage

echo "Running the .NET tests with coverage..." >&2
dotnet test --results-directory "$PWD/$results" \
  --coverage --coverage-output-format cobertura --coverage-settings "$PWD/tests/coverage.config.xml"

echo "Running the web tests with coverage..." >&2
(cd web && npm run test:coverage)

dotnet tool run reportgenerator \
  "-reports:$results/*.cobertura.xml;web/coverage/cobertura-coverage.xml" \
  "-targetdir:$report" \
  "-reporttypes:Badges;MarkdownSummaryGithub;TextSummary" \
  "-title:Tree Editor" >&2

percent=$(sed -n 's/^  Line coverage: \([0-9.]*\)%$/\1/p' "$report/Summary.txt")
color=$(awk -v p="$percent" 'BEGIN {
  print p >= 90 ? "brightgreen" : p >= 80 ? "green" : p >= 70 ? "yellowgreen" : p >= 60 ? "yellow" : p >= 50 ? "orange" : "red"
}')
cp "$report/badge_shieldsio_linecoverage_$color.svg" "$report/coverage.svg"

cat "$report/Summary.txt"
