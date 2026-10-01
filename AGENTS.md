# AGENTS.md

Working rules for coding agents in this repository. For deeper reference:

- `CLAUDE.md`: the strategy rules and the module map.
- `README.md`: every parameter, and where the trade CSV is written.
- `docs/architecture.md`: the AI trend filter, its HTTP contract and the diagrams.
- `docs/debugging-in-rider.md`: stepping through the running cBot on macOS.

## What this is

`VWAPTradeAI` is a cTrader cBot (C#, cAlgo API, `net6.0`) that trades the **VWAP Strong** strategy
(`docs/VWAP_Strong_V2.pdf`) on **M5 only**, on Japan time.

Once per closed bar:

1. **Strong?** `VwapStack.ResolveSide(close, daily, weekly)` returns Buy for
   `close > daily > weekly`, Sell for `close < daily < weekly`, otherwise None and nothing happens.
2. **Signal?** A candle pattern for that side touches the daily VWAP (the yellow line):
   `LevelPatternMatcher` with `HanJinSignals26`.
3. **Mark.** `SignalMarkers` marks the signal on the chart; the marker stays whether or not the
   order goes out.
4. **AI trend gate, only with `启用AI趋势过滤` on** (off by default; then step 5 follows at once).
   `AiChartshots` takes `Chart.TakeChartshot()` once the chart shows the signal's bar (waiting in
   place up to 2 s in a backtest, or over the following ticks of the same bar) and *before* step 3
   draws the marker, then the picture goes to `AiTrendFilter.Submit`, which returns immediately. The local TrendAssessmentModel service
   answers some seconds later; back on the cBot thread the signal must still be the last closed
   bar and pass `TrendDirectionGate` (Buy + UP, or Sell + DOWN, and a daily VWAP that is not
   FLAT). Anything else, including any failure, is a logged rejection. In a visual backtest,
   `AiTrendFilter.AssessAndWait` takes the place of `Submit` and waits for the answer, so the
   backtest pauses at each signal.
5. **Order gates**, in order: open position on this level, price still between the signal's stop
   and target, sizing (`OrderPlanner`), broker. There is no time-of-day or weekday gate.
   `OrderExecutor.TryEnter` returns whether an order went out, and logs why not. The stop and the
   target come from the signal; the entry is the ask/bid when `TryEnter` runs (as the next bar
   opens, or after the AI's answer), and the size follows from the actual entry-to-stop distance.
6. **Record:** a trade gets a trade CSV row and a numbered chart screenshot when it opens, and
   another CSV row when it closes. Both are subscribers to `OrderExecutor.PositionOpened` /
   `PositionClosed`, wired in `OnStart`.

## Rules that protect the strategy

Breaking one of these changes trading results silently, so treat them as fixed:

- **With the AI filter off, a signal trades at once, and only once.** `OnBar` calls
  `OrderExecutor.TryEnter` with the signal of the bar that just closed. Nothing waits, and the bot
  has no network traffic. Off is the default, and this path must stay exactly as it is.
- **With the AI filter on, the only other caller of `TryEnter` is `AiTrendFilter`**, on the cBot
  thread, for a signal that is still the last closed bar. It is fail closed: a missing picture, a
  failed or invalid answer, an unreadable chart or a stale signal never trades.
- **The AI's picture is taken before the signal's marker is drawn, and only once the chart shows
  the signal's bar.** A model that sees the strategy's own BUY/SELL mark is biased towards it, and
  one that sees an old chart judges the wrong moment. The marker is drawn in
  `AssessPicturedSignal`, after `AiChartshots` has taken the picture; keep it there. A signal with
  no current picture is rejected, never sent with an old one.
- **Live and demo, the model wait never runs on the cBot thread**, and nothing but the final
  `BeginInvokeOnMainThread` continuation touches cTrader. In a visual backtest it is the opposite
  on purpose: `AssessAndWait` holds the cBot thread so the backtest's clock cannot run past the
  signal's bar. Either way no price is read before the wait: `TryEnter` reads the Ask/Bid
  afterwards.
- **The AI gate is `TrendDirectionGate` and nothing more.** Don't add a confidence threshold or a
  "VWAP must slope the signal's way" rule; both were considered and left out on purpose.
- **This cBot never talks to Ollama.** The model, the prompt and the JSON schema belong to the
  TrendAssessmentModel service; here there is only `TrendAssessmentClient` and its contract.
- **The AI filter needs a chart on screen:** live, demo and visual backtests. `StartupCheck`
  refuses it in non-visual backtests and optimization, where there is nothing to photograph.
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
| `Orders/` | `RiskBudget`, `OrderPlanner` (sizing), `OrderExecutor` (gates + placing + trade events). |
| `Broker/` | The trading boundary: ports `IBroker`, `ISymbolModel`; cAlgo adapters `CAlgoBroker`, `CAlgoSymbolModel`. |
| `TradeLog/` | Trade CSV (`TradeCsvColumns`, `TradeCsvLogger`, `TradeCsvFile`, `CsvCell`, `TradeResultR`). |
| `Chart/` | `VwapLines`, `SignalMarkers`. Drawing only. |
| `Chartshots/` | `EntryChartshots` (takes `Chart.TakeChartshot()` per entry), `ChartshotFolder` (`~/Documents/TakeChartshot`, numbered `1.png`, `2.png`, …; pure), and for the AI: `AiChartshots` (when the picture is taken; pure), the port `IChartCamera` and its adapter `CAlgoChartCamera`. |
| `TrendAssessment/` | The optional AI trend filter, all pure: `AiTrendFilter` (the flow), `TrendDirectionGate` (the rule), `TrendAssessmentClient` and `TrendAssessmentReply` (the service's HTTP contract), `TrendAssessmentRecorder` (`~/Documents/TrendAssessment`). |
| `Models/` | Every data type, suffixed `Model`. Pure data only. |

The design patterns in use (Composition Root, Adapter, Observer, Strategy as a table), where each
sits and the ones deliberately not used are listed in `CLAUDE.md` under "Design patterns". Name a pattern in a `Pattern:` comment on the
class that plays it; don't add one the code doesn't need.

Conventions:

- **Pure by default.** A class without `using cAlgo.API` is pure and unit tested; keep new rules
  that way. cAlgo is touched only by the Robot, `Chart/`, the `Bars` readers,
  `EntryChartshots`, `CAlgoChartCamera` and the `Broker/` adapters. `Models/` is pure data; the test project links it wholesale.
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
  account, or backtest it → read the Log tab and the CSVs. The AI filter can be checked on a
  demo or live chart, or in a visual backtest, with the TrendAssessmentModel service running and
  the chart visible.
- **The AI service contract** is checked by two opt-in tests (`TrendAssessmentServiceTests`,
  `RUN_AI_SERVICE_TESTS=1`), which need the service running; see `CLAUDE.md`.

## cTrader behaviour worth knowing

- **Parameter defaults:** `[Parameter(DefaultValue = …)]` only affects new instances. Existing
  instances keep their saved values; recreate the instance to see a new default.
- **Instances:** each running instance (symbol/timeframe) has its own state and its own `OnStart`.
- **Access rights:** `AccessRights.FullAccess` is required because the bot writes its CSVs
  and entry screenshots and, with the AI filter on, calls the AI service on this machine. Output
  goes under `~/Documents`:
  - `trading_reports` for backtests
  - `simulate_trading_reports` for demo
  - `release_trading_reports` for live
  - `TakeChartshot` for the entry screenshots, whatever the running mode
  - `TrendAssessment` for the AI's input pictures and answers, only with `保存AI评估截图` on
  
  The trade CSV is `VWAPTradeAIs.csv` by default. If cTrader reports a sync conflict over full
  access, keep the local source.
- **Chart objects** persist after the bot stops; nothing clears them in `OnStop`.
