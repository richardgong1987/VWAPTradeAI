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

**By default a signal trades at once.** With `启用AI趋势过滤` off (the default), `OnBar` hands the
closed bar's signal straight to `OrderExecutor.TryEnter`; nothing waits for a person, and the bot
neither listens to nor sends anything over the network. The order still passes every
`OrderExecutor` check: no open position on the level, price still between the stop and the target,
valid sizing, broker acceptance. There is no order window: a signal may trade at any hour the
market is open, Monday included. Live, backtest and optimization all trade the same way.

**The optional AI trend filter puts one gate in front of that.** With `启用AI趋势过滤` on, a signal
reaches `OrderExecutor` only after a local vision model has looked at a screenshot of the chart
and its answer passes `TrendDirectionGate`: a Buy needs an UP trend, a Sell a DOWN trend, and the
daily VWAP must not be FLAT. The model runs behind the separate **TrendAssessmentModel** service
(`http://127.0.0.1:8787` → Ollama `http://127.0.0.1:11434` → `gemma3:27b`); this cBot knows only
that service's HTTP contract, never Ollama, the prompt or the model. The answer takes several
seconds, so it is awaited off the cBot thread and applied back on it
(`BeginInvokeOnMainThread`). It is fail closed: no picture, no valid answer, an unreadable chart
or a signal that went stale all mean no trade. In a **visual backtest** the filter runs too, but
the other way round: the backtest's clock would not wait, so `AiTrendFilter.AssessAndWait` holds
the cBot thread until the answer is in (the backtest pauses). In every mode the picture is only
taken once the chart shows the signal's bar (`AiChartshots`): a fast visual backtest draws its
chart behind the cBot, and a chart can be scrolled back. Non-visual backtests and optimization have
no chart, so `StartupCheck` refuses the filter there. The full design is in `docs/architecture.md`.

**The stop and the target belong to the signal; the entry is the market price.** The stop sits
`StopOffsetTicks` beyond the pattern's own stop; the target is `TakeProfitR × R` measured from the
signal close, where R is the signal close to the stop. The market order enters at the ask (long)
or bid (short) as the next bar opens, and is sized so the actual entry-to-stop distance loses
`RiskPct`%, so the spread or a gap leaves an R:R slightly off `TakeProfitR`. If price has already
reached the stop or the target, no order is placed. Nothing manages the position after it opens.

`StartupCheck` stops the bot on any timeframe other than M5.

Everything runs on Japan time (`[Robot(TimeZone = TimeZones.TokyoStandardTime)]`). The indicator
periods (`Vwap/VwapPeriod.cs`): the daily VWAP accumulates 06:00 → 06:00 the next morning, the
weekly from Monday 06:00. Every bar accumulates, so a VWAP value is never blank.

`VWAPTradeAI.cs` is the Robot lifecycle shell and the composition root. `OnStart` reads the
parameters, validates them (`StartupCheck`), builds the pipelines — `BuildSignalPipeline`,
`BuildChartDrawing`, `BuildTradeLog`, `BuildEntryChartshots`, `BuildOrderExecutor` and, only with
the AI filter on, `BuildAiTrendFilter` — and subscribes everything that follows a trade to
`OrderExecutor`'s events in one block. The flows:

- per closed bar (`OnBar`), AI filter off: `SignalDetector.DetectOnClosedBar` → chart marker
  (`SignalMarkers.Draw`, for every signal, whether or not its order goes out) →
  `OrderExecutor.TryEnter`;
- per closed bar, AI filter on: `AiChartshots.OnNewBar` (settles a signal whose picture never
  came) → `SignalDetector.DetectOnClosedBar` → `SignalMarkers.Draw` → `AiChartshots.OnSignal`.
  Once the chart shows the bar after the signal's (at once, or on a later tick via `OnTick`; a
  backtest pauses 100 ms after each look that finds the chart behind), `Chart.TakeChartshot()`
  runs **after** the marker, so the picture shows which signal was judged →
  `AssessPicturedSignal`: `AiTrendFilter.Submit`, which returns at once. Later, on the cBot thread:
  `AiTrendFilter.Complete` → signal still the last closed bar? → `TrendDirectionGate` →
  `OrderExecutor.TryEnter`, which reads the Ask/Bid of that moment. In a visual backtest
  `AiTrendFilter.AssessAndWait` replaces `Submit`: it waits for the answer and completes before
  the handler returns;
- per trade: `OrderExecutor.PositionOpened` → trade CSV entry row + a numbered chart screenshot
  (`EntryChartshots.Take`); `OrderExecutor.PositionClosed` → trade CSV close row.

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
- `Chart/` — `VwapLines` draws the three VWAP lines, `SignalMarkers` a marker on every detected signal
  (drawn whether or not its order goes out).
- `Chartshots/` — a screenshot of the chart for every order that goes out. `EntryChartshots` calls
  `Chart.TakeChartshot()` (null when the chart is not visible: non-visual backtest, optimization)
  and never lets a failed screenshot stop the cBot; `ChartshotFolder` (pure, unit tested) owns
  `~/Documents/TakeChartshot` and the numbering `1.png`, `2.png`, …. With
  `启动时清空交易记录CSV和截图` on it deletes the earlier runs' numbered pictures at start-up and
  starts again from 1, matching the reset trade CSV; off, numbering carries on after the highest
  number in the folder. Either way a picture is never overwritten.
  The same folder holds the AI's picture: `AiChartshots` (pure, unit tested) decides *when* it is
  taken, through the port `IChartCamera` and its cAlgo adapter `CAlgoChartCamera`. It waits until
  the chart shows the bar after the signal's (`Chart.LastVisibleBarIndex`), checking on each tick
  of the same bar, and scrolls the chart to the newest bar once after two ticks. A visual
  backtest's chart only catches up between the cBot's handlers (holding the thread inside `OnBar`
  never helped), so in a backtest each look that finds the chart behind ends with a 100 ms pause.
  It gives up with `AI chart not current` at the next bar, or earlier: live after a minute of
  market time, in a backtest after 50 pauses (5 s), because backtest market time races. It hands
  every signal on exactly once, with the picture or null. The marker is already drawn when it
  starts, so the picture shows it.
- `Orders/` — `RiskBudget` (how much account currency one trade may lose), `OrderPlanner` (sizing/geometry
  from the signal and the entry price, and every reason a plan is rejected) — all pure,
  unit tested — and `OrderExecutor`: `TryEnter` checks the order gates, reads the quote, places
  the order through `IBroker`, and returns whether an order went out, with the gate's own words
  as `rejectReason` when not (also logged). It is called by the Robot's `OnBar`, or by
  `AiTrendFilter` when the AI filter is on. It raises `PositionOpened` / `PositionClosed` (this
  strategy's positions only) and knows nothing of who listens. Unit tested against a fake broker.
- `TrendAssessment/` — the optional AI trend filter, all of it free of cAlgo and unit tested.
  `TrendDirectionGate` is the pure PASS/REJECT rule. `TrendAssessmentClient` is the HTTP link to
  the TrendAssessmentModel service (`POST /v1/assessments`, `POST /v1/warmup`) and never throws
  for a failed request; `TrendAssessmentReply` reads the service's reply strictly into a
  `TrendAssessmentResultModel`. `AiTrendFilter` is the flow: `Submit` (live, demo) sends the picture and
  returns, the answer comes back through `BeginInvokeOnMainThread`, and only a signal that is
  still the last closed bar and passes the gate goes to `TryEnter`; `AssessAndWait` (visual
  backtest) does the same but waits for the answer, so the backtest pauses. `TrendAssessmentRecorder`
  (only with `保存AI评估截图` on) keeps the exact PNG and a JSON file per assessment under
  `~/Documents/TrendAssessment`; with `启动时清空AI评估截图` on (off by default) it deletes the
  earlier runs' `.png` and `.json` files at start-up. This is a different picture from
  `Chartshots/`: that one is taken after the entry, as a trade record.
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
  `OrderPlanModel` (sizing, plus a reference to the signal it was made from), the broker's facts
  (`PositionEntryModel`, `PositionCloseModel`, `PositionCloseReasonModel`,
  `BrokerOrderResultModel`), `TradeLevelModel`, `TradeSettingsModel`, `VwapSampleModel`,
  `TradeDirectionModel`, `TradeRecordModel` (one CSV row), and the AI filter's
  `TrendModel` (Up/Down/Sideways), `DailyVwapDirectionModel` (Rising/Falling/Flat),
  `TrendAssessmentOutcomeModel` (Assessed/Unreadable/Unavailable), `TrendAssessmentResultModel`
  and `TrendAssessmentRecordModel`. The test project links `Models/*.cs` wholesale, so a cAlgo
  reference here breaks the tests at once.

Rule of thumb: classes with no `using cAlgo.API` are pure and testable; keep them that way. cAlgo
is touched only by the Robot, `Chart/`, the `Bars` readers (`VwapSeries`, `SignalDetector`),
`EntryChartshots`, `CAlgoChartCamera` and the `Broker/` adapters; none of those is linked into the
test project. The AI's picture goes through `IChartCamera`, which keeps `AiChartshots` and
`TrendAssessment/` free of cAlgo.

## Design patterns

Each pattern is also named in a `Pattern:` comment on the class that plays it.

| Pattern | Where | Why |
| --- | --- | --- |
| Composition Root | `VWAPTradeAI` (the Robot) | The one place the pipelines are created and wired together, so each class receives what it needs instead of reaching for it. |
| Adapter | `CAlgoBroker : IBroker`, `CAlgoSymbolModel : ISymbolModel`, `TrendAssessmentClient` | cAlgo and HTTP types stop at the edge, so orders, sizing, the CSV and the AI flow are unit tested with fakes. |
| Observer | `OrderExecutor.PositionOpened` / `PositionClosed`, wired in `OnStart` | The CSV and the chart screenshot follow a trade without the executor knowing them. |
| Strategy (as a table) | `LevelPatternMatcher` rules, `TradeCsvColumns` | Each row carries its own behaviour; adding a pattern or a column is one line, and the two sides/the header and rows cannot drift apart. |

Deliberately not used: a Chain of Responsibility for the order gates (a few guard clauses in
`TryEnter` read better than gate classes); interfaces with a single implementation and no testing
need (`OrderPlanner`, `TradeCsvLogger`, `VwapSeries`).

Conventions worth knowing before renaming things:

- **Reader / rule pairs.** Where a rule needs market data, the reading and the rule are separate
  classes, as in `VwapSeries`/`VwapCalculator`. The reader touches `Bars`; the rule stays pure and
  unit tested. Keep new work in that shape.
- **`SignalSideModel` (None/Buy/Sell) and `TradeDirectionModel` (Long/Short) are deliberately
  separate.** The first is what a bar suggests, and may be None; the second is a side an order is
  actually sent with, and cannot be. Merging them would push `None` into the order layer.
- **Threads.** Strategy state (`OrderExecutor`, the broker, the chart) is only touched on the cBot
  thread. Live and demo, with the AI filter on, the HTTP wait runs on the thread pool and comes
  back through `BeginInvokeOnMainThread`; never call a trading or chart API from that continuation
  directly, and never wait for the model on the cBot thread. The one deliberate exception is the
  visual backtest, where `AssessAndWait` blocks the cBot thread so the backtest's clock stands
  still; the request itself still runs on the thread pool, so the wait cannot deadlock.
- **The AI never decides a trade's terms.** It only answers whether the visible chart supports
  the signal's side. Entry price, stop, target, sizing and every order gate stay in `Orders/`, and
  the quote is read after the model has answered, never before. Confidence is logged, not used.
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

# Opt-in: the cBot's HTTP client against the real TrendAssessmentModel service (must be running)
RUN_AI_SERVICE_TESTS=1 AI_SERVICE_TEST_PNG=/path/to/chart.png \
  dotnet test "tests/VWAPTradeAI.Tests/VWAPTradeAI.Tests.csproj" --filter "FullyQualifiedName~TrendAssessmentServiceTests"
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
CSV and the entry screenshots under `~/Documents` and, with the AI filter on, calls the
TrendAssessmentModel service on this machine. Don't add other network or file access on the
strength of it.

## Conventions

- Spaces in the project/solution/file names are intentional (cTrader convention) — always
  quote paths in shell commands.
- `bin/`, `obj/`, `.idea/`, `*.user`, and generated `*.algo` files are git-ignored; commit
  only the `.cs`, `.csproj`, and `.sln`.
