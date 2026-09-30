# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A **cTrader cBot** (automated trading robot) written in C# against the cAlgo API, targeting
`net6.0`. The strategy is **VWAP Strong** (`docs/VWAP_Strong_V1.1.docx`), a VWAP break-and-reverse
on **M5 only**. The daily VWAP is the key level (the yellow line): a closed bar whose candle
pattern (pinbar / engulfing / fractal / harami) touches it becomes an entry, sized against a
per-trade risk budget. A signal needs:

1. **Stack** — only `close > daily > weekly` may go long, only `close < daily < weekly` may short
   (`VwapStack.ResolveSide`). Such a bar is called Strong.
2. **Pattern** — a candle pattern for that side touching the daily VWAP (`LevelPatternMatcher`).

**A signal never trades by itself.** Trading is manual: the bot stores the signal as pending
under a new `signal_id` and announces it (`trade_opportunity`) through the WebSocket relay
(`rustwebsocket/`) to the desktop app (`rustapp/`). Only when the user presses **Place Trade** and
the app sends back `execute_trade` with that `signal_id` does the bot attempt the order, with the
original `SignalModel`, still through every `OrderExecutor` check: no open position on the level,
price still between the stop and the target, valid sizing, broker acceptance. There is no order
window: an approval may trade at any hour the market is open, Monday included. An approval is
honoured once, and there is no approval window either: a signal stays approvable until it is
approved or dismissed (or the cBot restarts), however late.

**The stop and the target belong to the signal; the entry is the price at approval.** The stop
sits `StopOffsetTicks` beyond the pattern's own stop; the target is `TakeProfitR × R` measured
from the signal close, where R is the signal close to the stop. Both stay at those prices however
long the user takes. The market order enters at the ask (long) or bid (short) when the approval is
applied, and is sized so the actual entry-to-stop distance loses `RiskPct`%, so a late entry has a
smaller or larger R:R than `TakeProfitR`. Once price has reached the stop or the target, the
approval is rejected. Nothing manages the position after it opens; when it closes, the app is
told (`trade_profit` / `trade_loss` / `trade_breakeven`).

**Backtests are approved by hand too, but never wait.** The backtest keeps running while a signal
is pending; an approval is applied on the next simulated tick (`OnTick`) and enters at that
tick's price, by the rule above. Optimization never connects, so it opens no trades; nor does
`中继环境` = Off.

**Relay environments** are defined once in `Notifications/RelayEnvironments.cs`: **Local**
(`ws://localhost:8080/ws`, the default, for testing) and **Production**
(`wss://webhook.raku-den.net/ws`, the deployed relay the boss's app listens to). The default keeps
test runs away from the boss. `StartupCheck.FindRelayError` refuses Production without
`访问密钥`, which the link sends as `Authorization: Bearer <key>`. Each person has their own key
in the relay's `RELAY_ACCESS_KEYS` (see `rustwebsocket/relay.env.example`), and the key picks a
room: a person's cBot and app hear each other and nobody else, so the boss's app never shows, and
can never approve, a signal from someone else's cBot.

`StartupCheck` stops the bot on any timeframe other than M5.

Everything runs on Japan time (`[Robot(TimeZone = TimeZones.TokyoStandardTime)]`). The indicator
periods (`Vwap/VwapPeriod.cs`): the daily VWAP accumulates 06:00 → 06:00 the next morning, the
weekly from Monday 06:00. Every bar accumulates, so a VWAP value is never blank.

`VWAPTradeAI.cs` is the Robot lifecycle shell and the composition root. `OnStart` reads the
parameters, validates them (`StartupCheck`), builds the pipelines — `BuildSignalPipeline`,
`BuildChartDrawing`, `BuildTradeLog`, `BuildOrderExecutor`, `BuildApprovalPipeline` — and
subscribes everything that follows a trade to `OrderExecutor`'s events in one block. The flows:

- per closed bar (`OnBar`): `SignalDetector.DetectOnClosedBar` → chart marker
  (`SignalMarkers.Draw`, for every signal, approved or not) → `ApprovalDesk.OnSignal` →
  `TradeApproval.OnSignal` (store + announce). Nothing waits for the decision, in a backtest
  either;
- per decision: relay text on the link's thread → `ApprovalDesk.ReceiveRelayText`
  (`TradeMessages.TryReadDecision` → `DecisionInbox.Post`) → cBot thread
  (`BeginInvokeOnMainThread` or the next `OnTick`, whichever comes first →
  `ApplyReceivedDecisions`) → `TradeApproval.Decide` → `OrderExecutor.TryEnter`;
- per trade: `OrderExecutor.PositionOpened` → trade CSV entry row;
  `OrderExecutor.PositionClosed` → trade CSV close row + `trade_profit` / `trade_loss` /
  `trade_breakeven` to the app.

It holds no rules of its own; anything resembling a decision belongs in one of the classes below.

## Module map

Each folder holds one responsibility; all data types live in `Models/` (suffixed `Model`):

- `StartupCheck.cs` (beside the Robot) — parameter validation. Pure, unit tested.
- `Vwap/` — computing VWAP values only: `VwapPeriod` (when the VWAP resets), `VwapCalculator`
  (pure accumulation) — both unit tested — plus `VwapSeries`, which reads the platform, caches one
  `VwapSampleModel` per closed bar and is the single source of VWAP values for drawing and signals.
- `Signals/` — what makes a signal. `SignalDetector` (the reader) returns the closed bar's signal
  or null. A signal needs a Strong bar (`VwapStack.ResolveSide(close, daily, weekly)` returns Buy
  or Sell) and a pattern for that side touching the daily VWAP. `LevelPatternMatcher` holds the
  pattern rules as one table per side (name, candles that must touch, where the stop goes) and
  returns a `PatternMatchModel`; `HanJinSignals26` is the pattern classifier itself (a port — see
  below).
- `Approval/` — `PendingTradeSignals` (the signals awaiting approval by `signal_id`; each is
  consumed at most once, with no time limit), `TradeApproval` (the rules: `OnSignal` stores and
  announces, never trades; `Approve` consumes the ID and calls `TryEnter` through the
  `TryEnterOrder` delegate, then reports the result; `Dismiss` consumes it without
  trading), `DecisionInbox` (hands the user's decisions from the link's thread to the
  cBot thread; the only thread-safe one) and `ApprovalDesk` (the facade the Robot calls: the
  clock, and relay text in). All pure and unit tested — `TradeApprovalTests` is the
  proof that detecting a signal places no order.
- `Notifications/` — `TradeMessages` (pure, unit tested: builds every JSON message the bot sends,
  reads the `execute_trade` / `dismiss_trade` decisions, classifies a close as
  profit/loss/breakeven) and `TradeNotificationClient`, the link to the relay. It uses .NET's
  `ClientWebSocket`, **not** `cAlgo.API.WebSocketClient`: that one can only be created on the cBot
  thread (its factory is `[ThreadStatic]`) and connects synchronously, so every reconnect would
  stall the bars. The link's
  tasks connect, reconnect every 5 s, write queued messages and read incoming ones; the cBot
  thread never waits on the network. No cAlgo dependency: tested against a local WebSocket
  server.
- `Chart/` — `VwapLines` draws the three VWAP lines, `SignalMarkers` a marker on every detected signal
  (drawn when it is announced, not when it trades).
- `Orders/` — `RiskBudget` (how much account currency one trade may lose), `OrderPlanner` (sizing/geometry
  from the signal and the approval-time price, and every reason a plan is rejected) — all pure,
  unit tested — and `OrderExecutor`: `TryEnter` checks the order gates, reads the quote, places
  the order through `IBroker`, and returns whether an order went out, with the gate's own words
  as `rejectReason` when not. Its only caller is
  `TradeApproval.Approve`. It raises `PositionOpened` / `PositionClosed` (this strategy's
  positions only) and knows nothing of who listens. Unit tested against a fake broker.
- `Broker/` — the boundary to cTrader's trading API: the ports `IBroker` (clock, equity, quote,
  positions, market orders, closes) and `ISymbolModel` (symbol facts for sizing), and their cAlgo
  adapters `CAlgoBroker` and `CAlgoSymbolModel`, which translate and decide nothing. The adapters
  are the only files outside the Robot, `Chart/` and the two `Bars` readers that touch cAlgo.
- `TradeLog/` — `TradeCsvColumns` is the single declarative table of CSV columns (name + how to
  read it), so the header and every row are generated from one list and cannot drift apart;
  `TradeCsvLogger` decides what facts go in a row and keeps each open position's entry plan until
  its close row; `TradeCsvFile` owns the file itself (folder per running mode, path, header,
  append). A file written with a different header is renamed aside at start-up and a fresh one
  started, so changing the columns needs no migration. `CsvCell` formats one cell; `TradeResultR`
  is the R a closed trade achieved. All unit tested.
- `Models/` — pure data only: `SignalModel` (what the bar showed), `PatternMatchModel`,
  `PendingTradeSignalModel` (a signal awaiting approval: `SignalId`, the original `SignalModel`,
  `DetectedAtUtc`), `ApprovalOutcomeModel`, `TradeMessageModel` (one relay message as JSON),
  `OrderPlanModel` (sizing, plus a reference to the signal it was made from), the broker's facts
  (`PositionEntryModel`, `PositionCloseModel`, `PositionCloseReasonModel`,
  `BrokerOrderResultModel`), `TradeLevelModel`, `TradeSettingsModel`, `VwapSampleModel`,
  `TradeDirectionModel`, `TradeRecordModel` (one CSV row). The test project links `Models/*.cs`
  wholesale, so a cAlgo reference here breaks the tests at once.

Rule of thumb: classes with no `using cAlgo.API` are pure and testable; keep them that way. cAlgo
is touched only by the Robot, `Chart/`, the `Bars` readers (`VwapSeries`, `SignalDetector`) and
the `Broker/` adapters; none of those is linked into the test project.

## Design patterns

Each pattern is also named in a `Pattern:` comment on the class that plays it.

| Pattern | Where | Why |
| --- | --- | --- |
| Composition Root | `VWAPTradeAI` (the Robot) | The one place the pipelines are created and wired together, so each class receives what it needs instead of reaching for it. |
| Adapter | `CAlgoBroker : IBroker`, `CAlgoSymbolModel : ISymbolModel`, `TradeNotificationClient` | cAlgo and WebSocket types stop at the edge, so orders, sizing and the CSV are unit tested with fakes. |
| Observer | `OrderExecutor.PositionOpened` / `PositionClosed`, wired in `OnStart` | The CSV and the app notice follow a trade without the executor knowing them. |
| Facade | `ApprovalDesk` | The Robot makes three calls; the approval rules, the thread hand-off and the JSON stay behind it. |
| Producer–Consumer | `DecisionInbox`, the outbox of `TradeNotificationClient` | The only safe crossing between the relay's threads and the cBot thread. |
| Strategy (as a table) | `LevelPatternMatcher` rules, `TradeCsvColumns` | Each row carries its own behaviour; adding a pattern or a column is one line, and the two sides/the header and rows cannot drift apart. |
| Factory Method (static) | `TradeMessages` | One method per message type builds it complete; no caller assembles protocol JSON. |

Deliberately not used: a Chain of Responsibility for the order gates (a few guard clauses in
`TryEnter` read better than gate classes); a State pattern for a pending signal (one flag,
consumed or not); interfaces with a single implementation and no testing need (`OrderPlanner`,
`TradeApproval`, `TradeCsvLogger`, `VwapSeries`).

Conventions worth knowing before renaming things:

- **Reader / rule pairs.** Where a rule needs market data, the reading and the rule are separate
  classes, as in `VwapSeries`/`VwapCalculator`. The reader touches `Bars`; the rule stays pure and
  unit tested. Keep new work in that shape.
- **`SignalSideModel` (None/Buy/Sell) and `TradeDirectionModel` (Long/Short) are deliberately
  separate.** The first is what a bar suggests, and may be None; the second is a side an order is
  actually sent with, and cannot be. Merging them would push `None` into the order layer.
- **Threads.** Strategy state (`TradeApproval`, `PendingTradeSignals`, `OrderExecutor`) is only
  touched on the cBot thread. Anything arriving from the relay link goes through
  `ApprovalDesk.ReceiveRelayText` and crosses over with `BeginInvokeOnMainThread` first; never call
  trading APIs from a WebSocket callback.
- `HanJinSignals26` keeps its name because it *is* a faithful port of that Pine library
  (`docs/design/hanjin-signals-26.md`); it changes when the original does.

## Build & run

```bash
# Build (from repo root)
dotnet build "VWAPTradeAI.sln"          # Debug
dotnet build "VWAPTradeAI.sln" -c Release
```

A successful build produces a `.algo` package under
`VWAPTradeAI/bin/<Config>/net6.0/`. The `.algo` file is the deployable
cBot artifact loaded by the cTrader desktop platform.

The cBot itself is validated by running it in cTrader's backtester/optimizer, not via a CLI
runner. Iteration loop: edit `.cs` → `dotnet build` → load/refresh the `.algo` in cTrader →
backtest.

Pure (framework-independent) helpers are unit-tested with xUnit under `tests/`:

```bash
./scripts/test.sh                                       # build cBot + run all tests
dotnet test "tests/VWAPTradeAI.Tests/VWAPTradeAI.Tests.csproj"      # tests only
```

The test project is intentionally **not** part of the `.sln` (which cTrader builds) and
targets `net10.0` rather than the cBot's `net6.0` — it links pure source files directly (via
`<Compile Include>`) instead of referencing the cBot project, so tests never pull in the
`cTrader.Automate` / cAlgo.API dependency. Keep new domain/risk logic pure so it can be
tested this way.

The `cTrader.Automate` NuGet package (versioned `*`) supplies the `cAlgo.API.*` assemblies;
restore happens automatically on build.

## Code structure

A cBot is a single class deriving from `cAlgo.API.Robot` in namespace `cAlgo.Robots`,
annotated with `[Robot(...)]`. The framework drives it through lifecycle overrides — there is
no `Main`:

- `OnStart()` — one-time setup (read parameters, attach indicators).
- `OnTick()` — runs on every price update; intraday/entry logic lives here.
- `OnBar()` — runs on each completed bar (override when the strategy is bar-based, e.g.
  computing the prior session's high/low).
- `OnStop()` — teardown.

User-tunable inputs are `public` properties decorated with `[Parameter(...)]`; these surface
in the cTrader UI and the optimizer. Trading actions and market data come from inherited
members (`ExecuteMarketOrder`, `Positions`, `Symbol`, `Bars`, `MarketSeries`, `Print`, etc.).

The bot runs with `[Robot(AccessRights = AccessRights.FullAccess)]` because it writes the trade
CSV under `~/Documents` and connects to the WebSocket relay. Don't add other network or file
access on the strength of it.

## Conventions

- Spaces in the project/solution/file names are intentional (cTrader convention) — always
  quote paths in shell commands.
- `bin/`, `obj/`, `.idea/`, `*.user`, and generated `*.algo` files are git-ignored; commit
  only the `.cs`, `.csproj`, and `.sln`.
