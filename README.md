# VWAPTradeAI

A cTrader cBot (C#, cAlgo API, `net6.0`) that finds **VWAP Strong** signals on **M5** charts and
trades them automatically.

The daily VWAP is the key level. When a closed bar forms a candle pattern (pinbar, engulfing,
fractal or harami) that touches it, and the VWAP stack points the same way, the bot enters at
market as the next bar opens, sizing the trade so that hitting the stop loses a fixed share of
account equity.

An optional **AI trend filter** can stand between the signal and the order: a local vision model
looks at the chart and the signal is traded only if the visible trend supports it. It is off by
default, and with it off the bot has nothing to do with AI. See [AI trend filter](#ai-trend-filter-optional).

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

**Order gates.** With the AI trend filter off (the default), a signal is traded at once, live, in
a backtest and in optimization alike, unless a gate stops it: an open position already on this level, price no longer between the signal's
stop and target, a size the broker does not accept, or the broker's refusal. The log says which.

**Stop, target and entry.** The stop and the target are fixed by the signal: the stop sits
`止损偏移点数` beyond the pattern's stop, the target `止盈目标` × R from the signal's close (R =
close to stop). The entry is the market price as the next bar opens, and the size is set so the
stop still loses `风险%`. If price has already reached the stop or the target by then, no order is
placed.

**Order.** The stop sits a few ticks beyond the pattern's own stop level. The target is a fixed
multiple of that risk (R). Both are set when the order opens; nothing moves them afterwards.

## AI trend filter (optional)

With `启用AI趋势过滤` on, a signal must be confirmed by a local vision model before it is traded:

```text
AI filter off (default):   signal → marker → order gates → order

AI filter on:              signal → marker
                                  → screenshot of the chart (with the marker on it)
                                  → local AI assessment (several seconds, in the background)
                                  → AI gate
                                  → order gates → order
```

**What the model is asked.** Two things about the chart it is shown: the overall trend of the
candlesticks (`UP`, `DOWN`, `SIDEWAYS`) and the slope of the solid yellow daily VWAP (`RISING`,
`FALLING`, `FLAT`). It does not find signals, predict prices, size or place orders.

**The AI gate.**

| Signal | Model says | Result |
| --- | --- | --- |
| Buy | trend `UP`, daily VWAP `RISING` or `FALLING` | pass |
| Sell | trend `DOWN`, daily VWAP `RISING` or `FALLING` | pass |
| any | daily VWAP `FLAT` | no trade |
| any | trend `SIDEWAYS`, or against the signal | no trade |
| any | chart unreadable, service or model unavailable, timeout, invalid answer | no trade |

The model's confidence numbers are logged for later evaluation and are not part of the rule.

**It fails closed.** If the chart cannot be captured (it must be visible on screen), the service
is down or slow, or the answer arrives after the next bar has closed, the signal is not traded and
the log says why.

**The order itself is unchanged.** After the model passes a signal, the bot reads the Ask/Bid of
that moment and applies the usual order gates, stop, target and sizing. Expect the entry some
seconds after the bar opens instead of at its first tick.

**Live, demo and visual backtests.** The model needs a picture of the chart, so the filter works
wherever there is a chart on screen:

- **Live and demo:** the bot carries on while the model thinks, and the order goes out some
  seconds after the bar opens.
- **Visual backtest:** a backtest's clock does not wait for anyone, so here the bot waits: the
  backtest pauses at every signal until the model has answered (several seconds each), and the
  signal is decided on its own bar. This is the way to see how the model judges past charts. A
  fast visual backtest draws its chart behind the bot, so before each picture the bot waits until
  the chart shows the signal's bar (see below), and rejects the signal if it never does.
- **Non-visual backtest and optimization:** there is no chart, so the bot refuses to start with the
  filter on.

With the filter off, every backtest and optimization runs exactly as before.

**The picture must show the signal's bar.** The chart can lag behind the bot (a fast visual
backtest) or be scrolled back. So the bot only takes the AI's picture once the chart shows the
bar after the signal's, which means the signal's bar is complete on screen:

1. It looks at once. If the chart lags, it looks again on every tick of the same bar. After two
   ticks it scrolls the chart to the newest bar once, in case it was scrolled back.
2. In a visual backtest each look that finds the chart behind ends with a 100 ms pause, so the
   backtest slows down for that bar and the chart gets real time to catch up. Holding the
   backtest inside one handler does not work: the chart only moves on between the bot's handlers.
3. If the chart never shows the bar, the signal is rejected: `AI chart not current`. Live that is
   after a minute; in a visual backtest after 50 pauses (5 seconds of real time), because a minute
   of backtest time can pass in a few milliseconds; either way at the latest when the next bar
   opens.

Each picture is logged with the bars the chart showed, for example
`AI chart picture | SignalBar: 4521 | LastVisibleBar: 4522 | FirstVisibleBar: 4380 | Waited: 300 ms, 3 ticks`.
When the picture is only taken on a later tick, the backtest has moved on by those ticks, so the
entry is a little after the bar's opening price, as it would be live.

**What must be running.** The model is served by a separate project,
[TrendAssessmentModel](https://github.com/richardgong1987/TrendAssessmentModel), which this bot
calls over HTTP on the same machine:

```text
VWAPTradeAI → TrendAssessmentModel http://127.0.0.1:8787 → Ollama http://127.0.0.1:11434 → gemma3:27b
```

Ollama and `gemma3:27b` are already installed on the trading machine. Start the service before
turning the filter on:

```bash
cd ~/PycharmProjects/TrendAssessmentModel
.venv/bin/python -m trend_assessment
```

At start-up the bot asks the service to load the model and logs `AI service ready`, or the reason
it is not. Each decision is logged as `AI trend accepted`, `AI trend rejected`,
`AI trend unreadable` or `AI trend unavailable`.

**Keeping the AI's input.** With `保存AI评估截图` on, every assessment saves the exact picture the
model was sent, and a JSON file with the answer, the gate's decision and whether an order went
out, under `~/Documents/TrendAssessment`. They accumulate across runs unless
`启动时清空AI评估截图` is on, which deletes the earlier runs' pictures and JSON files at start-up.
They are separate from the [entry screenshots](#entry-screenshots), which are taken after a trade
opens.

**Picking recordings to label.** While the bot runs, **Shift+click** a signal's marker (or its
candle) and that signal's picture and JSON move to `~/Documents/TrendAssessmentEval`, the folder
you label for evaluation and training. The log says `AI assessment moved to the evaluation set`,
or why not. It works for the signals of the running bot only: not after a restart, and not once a
visual backtest has ended (pause it instead). Nothing in that folder is ever overwritten.

**Step by step.** [docs/testing-the-ai-trend-filter.md](docs/testing-the-ai-trend-filter.md)
walks through starting everything, the log lines to expect, what to check and what to do when
something fails. When the model's reading of a chart is wrong, see
[Correcting the model when it is wrong](https://github.com/richardgong1987/TrendAssessmentModel/blob/main/docs/CORRECTING_THE_MODEL.md)
in the TrendAssessmentModel project.

## Architecture

![System architecture](docs/architecture.svg)

The design of the AI trend filter, the HTTP contract between the two projects, the threading
model and all six diagrams are in [docs/architecture.md](docs/architecture.md). The diagrams'
editable source is [docs/architecture.drawio](docs/architecture.drawio).

## Parameters

The labels are the ones shown in cTrader.

| cTrader label | What it does |
| --- | --- |
| 订单标签 | Label put on every order the bot places. |
| 风险% | Share of equity one trade may lose at its stop. 0 = never trades. |
| 止盈目标 | Take-profit, in R (multiples of the stop distance). |
| **风控配置** | |
| 止损偏移点数 | Ticks the stop sits beyond the pattern's stop level. |
| **AI趋势判断** | |
| 启用AI趋势过滤 | Off by default. On, a signal is traded only after the local AI service confirms the trend. Live, demo and visual backtests. |
| AI服务地址 | Where the TrendAssessmentModel service listens. Default `http://127.0.0.1:8787`. |
| AI超时秒数 | How long to wait for one assessment before rejecting the signal. Default 30, at most 240. Keep it above the service's own limit (25 s). |
| 保存AI评估截图 | Off by default. On, keeps the AI's input picture and its answer under `~/Documents/TrendAssessment`. |
| 启动时清空AI评估截图 | Off by default. On, deletes the earlier runs' pictures and answers in `~/Documents/TrendAssessment` when the bot starts with `保存AI评估截图` on. |
| **开发调试** | |
| 启动时清空交易记录CSV和截图 | Empty the trade CSV and delete the numbered entry screenshots when the bot starts. |
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

With `启动时清空交易记录CSV和截图` off, trades accumulate across runs. If the existing file was written by
a build with different columns, it is renamed to `<name>.old-<yyyyMMdd-HHmmss>.csv` in the same
folder and a fresh file is started; the log says so on start-up.

## Entry screenshots

Every time an order goes out, the bot saves a screenshot of the chart to
`~/Documents/TakeChartshot`, named by number: `1.png`, `2.png`, and so on.

With `启动时清空交易记录CSV和截图` on (the default), the numbered pictures of earlier runs are
deleted at start-up and the run starts again from `1.png`, so the pictures line up with the rows
of the trade CSV. Files with any other name are left alone. With it off, numbering carries on
after the highest number already in the folder and nothing is overwritten.

All running modes and all instances share this one folder, so a bot starting with the reset on
also deletes the pictures another instance has saved there.

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

Two further tests send a blank picture and a real chart through the bot's HTTP client to the real
TrendAssessmentModel service. They are skipped unless asked for, and need the service running:

```bash
RUN_AI_SERVICE_TESTS=1 AI_SERVICE_TEST_PNG=/path/to/chart.png \
  dotnet test "tests/VWAPTradeAI.Tests/VWAPTradeAI.Tests.csproj" --filter "FullyQualifiedName~TrendAssessmentServiceTests"
```

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
| `VWAPTradeAI/TrendAssessment/` | The optional AI trend filter: the gate rule, the link to the AI service, the flow, the recorder. |
| `VWAPTradeAI/Models/` | Data types. |
| `tests/VWAPTradeAI.Tests/` | Unit tests for the pure classes. |
| `docs/` | Architecture: `architecture.md`, the editable `architecture.drawio` and its SVG exports. |

The full module map, the design patterns in use and the design rules are in [CLAUDE.md](CLAUDE.md).
