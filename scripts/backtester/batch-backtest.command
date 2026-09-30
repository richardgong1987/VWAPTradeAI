#!/usr/bin/env bash

set -euo pipefail

cd "$(dirname "$0")/.." || exit 1

REPO_ROOT="$(cd .. && pwd)"
SOLUTION="$REPO_ROOT/DayTradeSelf.sln"


python3 -m venv .venv
source .venv/bin/activate

pip3 install -r requirements.txt

# 回测跑的是 .algo 包，不是源码。拉取代码后必须重新编译，否则 cTrader 会加载上一次的
# 旧 .algo，回测结果与当前代码不符。编译本身会把 .algo 发布到仓库上一层
# （cTrader 加载 cBot 的目录），不需要额外拷贝。
dotnet build "$SOLUTION" -c Release

python3 run_conditions.py --env-file .env-prod
