#!/usr/bin/env bash
#
# Build the cBot and run the unit tests in one step.
# Run from anywhere: ./scripts/test.sh
#
set -euo pipefail

# Resolve the repository root from this script's location, so the script works
# regardless of the current working directory.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$REPO_ROOT"

SOLUTION="VWAPTradeAI.sln"
TEST_PROJECT="tests/VWAPTradeAI.Tests/VWAPTradeAI.Tests.csproj"

echo "==> Building cBot ($SOLUTION)"
dotnet build "$SOLUTION" -c Release

echo
echo "==> Running unit tests ($TEST_PROJECT)"
dotnet test "$TEST_PROJECT"

echo
echo "==> OK: build succeeded and all tests passed"
