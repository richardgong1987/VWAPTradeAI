# AGENTS.md

Working rules for coding agents in this repository. For deeper reference:

- `CLAUDE.md`: the strategy rules and the module map.
- `README.md`: every parameter, and where the trade CSV is written.
- `docs/debugging-in-rider.md`: stepping through the running cBot on macOS.

## What this is

`VWAPTradeAI` is a cTrader cBot (C#, cAlgo API, `net6.0`) that trades the **VWAP Strong** strategy
(`docs/VWAP_Strong_V2.pdf`) on **M5 only**, on Japan time.

Once per closed bar:

1. **Strong?** `VwapStack.ResolveSide(close, daily, weekly)` returns Buy for
   `close > daily > weekly`, Sell for `close < daily < weekly`, otherwise None and nothing happens.
2. **Signal?** A candle pattern for that side touches the daily VWAP (the yellow line):
   `LevelPatternMatcher` with `HanJinSignals26`.
3. **Pending, not traded.** `SignalMarkers` marks the signal on the chart, then
   `ApprovalDesk.OnSignal` → `TradeApproval.OnSignal` stores it under a new `signal_id` and sends
   `trade_opportunity` to the relay. Nothing is ordered; the marker stays whether or not it trades.

The bot carries on, in a backtest too; the decision arrives whenever the user makes it (there is
no time limit) and is applied on the cBot thread (`BeginInvokeOnMainThread`, or the next
`OnTick`).

Once per decision (`execute_trade` = Place Trade, `dismiss_trade` = Dismiss, read by
`ApprovalDesk.ReceiveRelayText` and handed to the cBot thread through `DecisionInbox`):

4. **Consume.** `TradeApproval.Decide` takes the pending signal once; a dismissal, or an unknown
   or repeated ID, never trades.
5. **Order gates**, in order: open position on this level, price still between the signal's stop
   and target, sizing (`OrderPlanner`), broker. There is no time-of-day or weekday gate.
   `OrderExecutor.TryEnter` returns whether an order went out, and why not. The stop and the
   target come from the signal; the entry is the ask/bid at that moment, and the size follows
   from the actual entry-to-stop distance.
6. **Record:** a traded signal gets a trade CSV row and a `trade_opened` to the app (a refusal
   sends `trade_rejected`). Its close sends `trade_profit` / `trade_loss` /
   `trade_breakeven`. The CSV and the close notice are subscribers to
   `OrderExecutor.PositionOpened` / `PositionClosed`, wired together in `OnStart`.

## Rules that protect the strategy

Breaking one of these changes trading results silently, so treat them as fixed:

- **A signal never places an order by itself.** `OrderExecutor.TryEnter` is only ever called from
  `TradeApproval.Approve`. Don't add another caller; `TradeApprovalTests` guards this.
- **Relay traffic is handled on the cBot thread.** The link reads on its own threads:
  `ApprovalDesk.ReceiveRelayText` parses there and hands over through `DecisionInbox` before
  anything else is touched. Don't switch
  the link to `cAlgo.API.WebSocketClient`: it only works on the cBot thread, which a backtest
  holds while it waits for approval. Networking is auxiliary and must never throw into, or block,
  the strategy.
- **Relay environments default to Local.** Production (`RelayEnvironments`) reaches the boss's
  app; never make it the default.
- **The signal bar is the last closed bar, `Bars.Count - 2`.** `OnBar()` fires when a new bar
  opens. Never use `Bars.Count - 1` for signal logic.
- **M5 only.** The strategy is specified on M5. `StartupCheck` stops the bot on any other timeframe.
- **VWAP periods.** The VWAP resets at 06:00 daily and Monday 06:00 weekly (`VwapPeriod`), and
  every bar accumulates.
- **Touching counts only the candles the pattern uses:** pinbar 1, engulfing 2, fractal and
  harami 3.
- **Harami** (`current [0]`, `previous [1]`, `earlier [2]`): `earlier` strictly contains `previous`
  by high and low. Then `current.Close > previous.High` is Buy, `current.Close < previous.Low` is
  Sell, anything else None. Candle body direction plays no part. Don't revert to the old
  "previous contains current, reverse the parent body" rule.
- **`HanJinSignals26` is a faithful port of a Pine library.** Change it only when the original
  changes.

## Architecture

The layers are right; keep them and don't add more. One folder, one responsibility:

| Folder | Holds |
| --- | --- |
| `VWAPTradeAI.cs` | Composition root: parameters, `OnStart` builds the pipelines and subscribes the trade observers; the lifecycle overrides forward to them. No rules. |
| `StartupCheck.cs` | Parameter validation. |
| `Vwap/` | Computing VWAP values: `VwapPeriod`, `VwapCalculator`, plus the reader `VwapSeries`. |
| `Signals/` | What makes a signal: `VwapStack` (Strong side), `LevelPatternMatcher` (pattern rule tables), `HanJinSignals26`, and the reader `SignalDetector`. |
| `Approval/` | `PendingTradeSignals` (pending signals by ID, consumed once), `TradeApproval` (the rules), `DecisionInbox` (decisions → cBot thread), `ApprovalDesk` (the facade the Robot calls). |
| `Notifications/` | `TradeMessages` (the relay JSON), `TradeNotificationClient` (the `ClientWebSocket` link, on its own threads), `RelayEnvironments`. |
| `Orders/` | `RiskBudget`, `OrderPlanner` (sizing), `OrderExecutor` (gates + placing + trade events). |
| `Broker/` | The trading boundary: ports `IBroker`, `ISymbolModel`; cAlgo adapters `CAlgoBroker`, `CAlgoSymbolModel`. |
| `TradeLog/` | Trade CSV (`TradeCsvColumns`, `TradeCsvLogger`, `TradeCsvFile`, `CsvCell`, `TradeResultR`). |
| `Chart/` | `VwapLines`, `SignalMarkers`. Drawing only. |
| `Models/` | Every data type, suffixed `Model`. Pure data only. |

The design patterns in use (Composition Root, Adapter, Observer, Facade, Producer–Consumer,
Strategy as a table, static Factory Method), where each sits and the ones deliberately not used
are listed in `CLAUDE.md` under "Design patterns". Name a pattern in a `Pattern:` comment on the
class that plays it; don't add one the code doesn't need.

Conventions:

- **Pure by default.** A class without `using cAlgo.API` is pure and unit tested; keep new rules
  that way. cAlgo is touched only by the Robot, `Chart/`, the `Bars` readers and the `Broker/`
  adapters. `Models/` is pure data; the test project links it wholesale.
- **Reader / rule pairs.** When a rule needs market data, one class reads `Bars` and another holds
  the rule, as in `VwapSeries`/`VwapCalculator`.
- **Keep separate types separate:**
  - `SignalModel` says what the bar showed; whether it trades is `OrderExecutor`'s call.
  - `SignalSideModel` (None/Buy/Sell) is what a bar suggests; `TradeDirectionModel` (Long/Short)
    is the side an order is sent with.
  - `OrderPlanModel` is sizing only, plus a reference to its signal. Don't copy signal fields into it.
- **Keep display out of logic.** Drawing, CSV text and `Print` messages stay out of the rule classes.

## Working rules

- **Keep the Robot class clean**, and give each responsibility its own class. Drawing, signal
  detection, order execution and CSV writing never share a class.
- **Expose only real strategy parameters.** Visual constants (marker offsets, font size, line
  styles) stay hard-coded.
- **Prefer simple C#** over abstractions. No interface without a second implementation or a
  real testing need. No new NuGet dependencies.
- **Comments:** explain why, in English. Existing Chinese comments stay unless you are already
  editing that line; don't bulk-translate.
- **User-facing text is Chinese on purpose:** parameter labels, CSV headers and CSV values such as
  多/空 and 盈利/亏损. Match it.
- **Trade CSV columns:** change them only in `TradeCsvColumns`. There is no migration: at start-up
  a file with a different header is renamed to `<name>.old-<yyyyMMdd-HHmmss>.csv` and a fresh one
  started, so old rows never sit under a header they don't match.

## Coding style

Java-style braces: the opening brace goes on the same line. Single-statement `if`s have no braces.

```csharp
public bool TryEnter(SignalModel signal) {
    if (_broker.HasOpenPosition(label))
        return false;
}
```

Don't reformat touched code to Allman braces. Name things for what they are (`OrderPlanner`,
`SignalMarkers`), not `Helper` or `Manager`.

## Build, test, run

```bash
dotnet build "VWAPTradeAI.sln"                                   # Debug
dotnet build "VWAPTradeAI.sln" -c Release
./scripts/test.sh                                              # Release build + all unit tests
dotnet test "tests/VWAPTradeAI.Tests/VWAPTradeAI.Tests.csproj"     # tests only
```

- **The build output** is `VWAPTradeAI/bin/<Config>/net6.0/VWAPTradeAI.algo`, which cTrader loads.
- **Paths:** spaces in them are intentional; always quote them.
- **The test project** (`net10.0`, xUnit) isn't in the solution. It links the pure source files
  with `<Compile Include>`, never a project reference. Link every new pure file there.
- **Runtime checks** happen in cTrader: build → refresh the bot in cTrader → run it on a demo
  account, or backtest it, with the relay and the app running → approve in the app → read the Log
  tab and the CSVs. A backtest never pauses for a signal; an approval enters at the price of that
  moment.

## cTrader behaviour worth knowing

- **Parameter defaults:** `[Parameter(DefaultValue = …)]` only affects new instances. Existing
  instances keep their saved values; recreate the instance to see a new default.
- **Instances:** each running instance (symbol/timeframe) has its own state and its own `OnStart`.
- **Access rights:** `AccessRights.FullAccess` is required because the bot writes its CSVs and
  connects to the WebSocket relay.
  Output goes under `~/Documents`:
  - `trading_reports` for backtests
  - `simulate_trading_reports` for demo
  - `release_trading_reports` for live
  
  The trade CSV is `VWAPTradeAIs.csv` by default. If cTrader reports a sync conflict over full
  access, keep the local source.
- **Chart objects** persist after the bot stops; nothing clears them in `OnStop`.
