# VWAPTradeAI

A cTrader cBot (C#, cAlgo API, `net6.0`) that finds **VWAP Strong** signals on **M5** charts and
trades them automatically.

The daily VWAP is the key level. When a closed bar forms a candle pattern (pinbar, engulfing,
fractal or harami) that touches it, and the VWAP stack points the same way, the bot enters at
market as the next bar opens, sizing the trade so that hitting the stop loses a fixed share of
account equity.

Strategy spec: `docs/VWAP_Strong_V2.pdf`. TradingView reference indicator for the VWAP lines:
`docs/vwap-v5-slim.pine`.

## How it trades

**M5 only.** The bot stops on start-up if the chart is any other timeframe.

**Japan time.** The daily VWAP runs 06:00 → 06:00 the next morning; the weekly VWAP starts Monday
06:00. Every bar counts. There is no order window: a signal can trade at any hour the market is
open, Monday included.

**Signal**, checked on each closed bar:

1. **Stack.** Long only if `close > daily > weekly`; short only if `close < daily < weekly`.
2. **Pattern.** A candle pattern for that side touches the daily VWAP.

**Order gates.** A signal is traded at once, live, in a backtest and in optimization alike, unless
a gate stops it: an open position already on this level, price no longer between the signal's
stop and target, a size the broker does not accept, or the broker's refusal. The log says which.

**Stop, target and entry.** The stop and the target are fixed by the signal: the stop sits
`止损偏移点数` beyond the pattern's stop, the target `止盈目标` × R from the signal's close (R =
close to stop). The entry is the market price as the next bar opens, and the size is set so the
stop still loses `风险%`. If price has already reached the stop or the target by then, no order is
placed.

**Order.** The stop sits a few ticks beyond the pattern's own stop level. The target is a fixed
multiple of that risk (R). Both are set when the order opens; nothing moves them afterwards.

## Parameters

The labels are the ones shown in cTrader.

| cTrader label | What it does |
| --- | --- |
| 订单标签 | Label put on every order the bot places. |
| 风险% | Share of equity one trade may lose at its stop. 0 = never trades. |
| 止盈目标 | Take-profit, in R (multiples of the stop distance). |
| **风控配置** | |
| 止损偏移点数 | Ticks the stop sits beyond the pattern's stop level. |
| **开发调试** | |
| 启动时清空交易记录CSV | Empty the trade CSV when the bot starts. |
| debug调试 | Call `Debugger.Launch()` in `OnStart`. See [Debugging](#debugging). |
| 输出文件名 | Trade CSV file name. An absolute path is used as is. |

## Trade log

Every trade is written to a CSV in `~/Documents`, in a folder picked by how the bot is running:

| Running as | Folder |
| --- | --- |
| Backtest | `~/Documents/trading_reports` |
| Demo account | `~/Documents/simulate_trading_reports` |
| Live account | `~/Documents/release_trading_reports` |

The full path is printed in the cTrader log at start-up, on the `CSV logger path` line.

With `启动时清空交易记录CSV` off, trades accumulate across runs. If the existing file was written by
a build with different columns, it is renamed to `<name>.old-<yyyyMMdd-HHmmss>.csv` in the same
folder and a fresh file is started; the log says so on start-up.

## Entry screenshots

Every time an order goes out, the bot saves a screenshot of the chart to
`~/Documents/TakeChartshot`, named by number: `1.png`, `2.png`, and so on. Numbering carries on
after the highest number already in the folder, so a restart or a new backtest never overwrites
a picture; empty the folder to start again from 1.

cTrader can only take a screenshot of a chart that is visible. In a non-visual backtest, in
optimization, or when the chart is not on screen, the screenshot is skipped and the log says so;
trading is not affected.

## Build and test

Needs the .NET 10 SDK and cTrader desktop.

```bash
dotnet build "VWAPTradeAI.sln"              # Debug
dotnet build "VWAPTradeAI.sln" -c Release   # Release
./scripts/test.sh                         # Release build + all unit tests
```

The build writes `VWAPTradeAI.algo` to `VWAPTradeAI/bin/<Config>/net6.0/`. Load or refresh it in
cTrader, then backtest.

The unit tests (`tests/VWAPTradeAI.Tests`, xUnit, `net10.0`) aren't part of the solution. They
compile the pure source files directly, so they never need cTrader.

## Debugging

Attach Rider to the process cTrader runs the bot in. Step-by-step guide:
[docs/debugging-in-rider.md](docs/debugging-in-rider.md).

## Project layout

| Folder | What's in it |
| --- | --- |
| `VWAPTradeAI/VWAPTradeAI.cs` | The cBot itself: reads parameters and wires the pieces together. |
| `VWAPTradeAI/StartupCheck.cs` | Refuses to start with bad parameters. |
| `VWAPTradeAI/Vwap/` | Computing the daily and weekly VWAP. |
| `VWAPTradeAI/Signals/` | Checks the stack and finds the candle pattern on the key level. |
| `VWAPTradeAI/Orders/` | Risk budget, sizing, stops, placing orders. |
| `VWAPTradeAI/Broker/` | The boundary to cTrader's trading API (orders, positions, symbol facts). |
| `VWAPTradeAI/TradeLog/` | The trade CSV: columns, writing, setting old files aside. |
| `VWAPTradeAI/Chart/` | VWAP lines and signal markers on the chart. |
| `VWAPTradeAI/Chartshots/` | A numbered screenshot of the chart for every entry. |
| `VWAPTradeAI/Models/` | Data types. |
| `tests/VWAPTradeAI.Tests/` | Unit tests for the pure classes. |

The full module map, the design patterns in use and the design rules are in [CLAUDE.md](CLAUDE.md).
