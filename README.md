# DayTradeSelf

A cTrader cBot (C#, cAlgo API, `net6.0`) that finds **VWAP Strong** trading opportunities on
**M5** charts and trades them **only when you approve them**.

The daily VWAP is the key level. When a closed bar forms a candle pattern (pinbar, engulfing,
fractal or harami) that touches it, and the VWAP stack points the same way, the bot announces a
trading opportunity to the desktop app (`rustapp/`) through the WebSocket relay
(`rustwebsocket/`). Nothing is traded until you press **Place Trade** there; the bot then enters
at market, sizing the trade so that hitting the stop loses a fixed share of account equity.

Strategy spec: `docs/VWAP_Strong_V2.pdf`. TradingView reference indicator for the VWAP lines:
`docs/vwap-v5-slim.pine`.

## How it trades

**M5 only.** The bot stops on start-up if the chart is any other timeframe.

**Japan time.** The daily VWAP runs 06:00 → 06:00 the next morning; the weekly VWAP starts Monday
06:00. Every bar counts. There is no order window: an approved signal can trade at any hour the
market is open, Monday included.

**Signal**, checked on each closed bar:

1. **Stack.** Long only if `close > daily > weekly`; short only if `close < daily < weekly`.
2. **Pattern.** A candle pattern for that side touches the daily VWAP.

**Manual approval.** A signal is never traded automatically:

```text
signal found → stored as pending under a signal_id → trade_opportunity → relay → app (alarm)
you press Place Trade → execute_trade(signal_id) → relay → cBot → order gates → market order
you press Dismiss     → dismiss_trade(signal_id) → relay → cBot → signal dropped
```

- The bot keeps the original signal; the app only sends its `signal_id` back.
- An approval counts once. A repeated approval for the same ID is ignored and logged.
- There is no time limit: a signal can be approved however late, until it is placed or
  dismissed. Its card stays in the app until then. A restarted cBot has forgotten its old
  signals, so their cards can no longer be placed.
- Approval asks for an attempt; the **order gates** still apply: no open position on this level,
  price still between the signal's stop and target, a valid size, and the broker's acceptance. The
  app is told `trade_opened`, or `trade_rejected` with the gate's reason.
- When a position closes, the app is told `trade_profit`, `trade_loss` or `trade_breakeven` with
  the position's net profit.
- If the relay is down, the bot logs it once, keeps retrying every 5 s in the background, and the
  strategy runs on unaffected; signals found meanwhile simply cannot be approved.

**Backtests are approved by hand too**, so you can judge whether the approvals are worth it, but
they **never pause**: with the relay and the app running, the backtest keeps going while a
signal waits in the app. Press Place Trade whenever you like and the order enters at the
backtest's price at that moment. Optimization never connects, so it opens no trades; for
unattended runs (e.g. the batch backtest scripts) set `中继环境` to Off — then no trade opens
either.

**Stop, target and entry.** The stop and the target are fixed by the signal: the stop sits
`止损偏移点数` beyond the pattern's stop, the target `止盈目标` × R from the signal's close (R =
close to stop). The entry is the market price when you approve, and the size is set so the stop
still loses `风险%`. If price has already reached the stop or the target by then, no order is
placed.

**Environments.** `中继环境` picks the relay, defined once in `Notifications/RelayEnvironments.cs`:
**Local** (default) is `ws://localhost:8080/ws`, a relay on this machine, for testing; **Production**
is `wss://webhook.raku-den.net/ws`, the deployed relay the boss's app listens to. Because the
default is Local, a test run never reaches the boss. The log says which one at start-up
(`Relay: Local (ws://localhost:8080/ws)`). The relay requires the access key `访问密钥`
(`Authorization: Bearer <key>`), pre-filled with the boss's key from
`rustwebsocket/relay.env.example`. Each person has their own key, and the key picks a room on the
relay: signals only reach the app that uses the same key, so enter your own key in both the cBot
and your app.

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
| **人工确认** | |
| 中继环境 | **Local** (default, `ws://localhost:8080/ws`, for testing), **Production** (`wss://webhook.raku-den.net/ws`, the boss's), or **Off** (signals only logged, never approved). |
| 访问密钥 | Your access key on the relay, default `myaccesscode` (the boss's). It picks the room: only an app with the same key sees this bot's signals. Production refuses to start without one. |
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

## Build and test

Needs the .NET 10 SDK and cTrader desktop.

```bash
dotnet build "DayTradeSelf.sln"              # Debug
dotnet build "DayTradeSelf.sln" -c Release   # Release
./scripts/test.sh                         # Release build + all unit tests
```

The build writes `DayTradeSelf.algo` to `DayTradeSelf/bin/<Config>/net6.0/`. Load or refresh it in
cTrader, then backtest.

The unit tests (`tests/DayTradeSelf.Tests`, xUnit, `net10.0`) aren't part of the solution. They
compile the pure source files directly, so they never need cTrader.

## Debugging

Attach Rider to the process cTrader runs the bot in. Step-by-step guide:
[docs/debugging-in-rider.md](docs/debugging-in-rider.md).

## Project layout

| Folder | What's in it |
| --- | --- |
| `DayTradeSelf/DayTradeSelf.cs` | The cBot itself: reads parameters and wires the pieces together. |
| `DayTradeSelf/StartupCheck.cs` | Refuses to start with bad parameters. |
| `DayTradeSelf/Vwap/` | Computing the daily and weekly VWAP. |
| `DayTradeSelf/Signals/` | Checks the stack and finds the candle pattern on the key level. |
| `DayTradeSelf/Approval/` | Pending signals and the manual approval workflow. |
| `DayTradeSelf/Notifications/` | The relay messages, and the WebSocket link to the relay. |
| `DayTradeSelf/Orders/` | Risk budget, sizing, stops, placing orders. |
| `DayTradeSelf/Broker/` | The boundary to cTrader's trading API (orders, positions, symbol facts). |
| `DayTradeSelf/TradeLog/` | The trade CSV: columns, writing, setting old files aside. |
| `DayTradeSelf/Chart/` | VWAP lines and signal markers on the chart. |
| `DayTradeSelf/Models/` | Data types. |
| `tests/DayTradeSelf.Tests/` | Unit tests for the pure classes. |
| `rustwebsocket/` | The WebSocket relay between the cBot and the app. |
| `rustapp/` | The desktop app that shows opportunities and sends approvals. |

The full module map, the design patterns in use and the design rules are in [CLAUDE.md](CLAUDE.md).
