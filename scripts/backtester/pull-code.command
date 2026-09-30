#!/bin/bash

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SOLUTION="$REPO_ROOT/DayTradeSelf.sln"

cd $REPO_ROOT

git pull --all
git reset --hard origin/main

dotnet build "$SOLUTION" -c Release

