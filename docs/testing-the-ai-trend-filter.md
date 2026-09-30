# Using and testing the AI trend filter

A step-by-step guide to running VWAPTradeAI with the AI trend filter on, and to checking that it
behaves as designed. How it works is in [architecture.md](architecture.md); this page is only
about running it.

Three things must be running, all on this machine:

```text
cTrader with the VWAPTradeAI cBot  →  TrendAssessmentModel (127.0.0.1:8787)  →  Ollama (127.0.0.1:11434) with gemma3:27b
```

With the filter off you need none of this: the cBot trades exactly as before.

## Before you start

Check once that the pieces are installed.

```bash
ollama list                                   # must list gemma3:27b
curl -s http://127.0.0.1:11434/api/version    # Ollama answers with its version
ls ~/PycharmProjects/TrendAssessmentModel/.venv/bin/python   # the service's environment exists
```

If the last one is missing, create it:

```bash
cd ~/PycharmProjects/TrendAssessmentModel
python3 -m venv .venv
.venv/bin/pip install -e ".[dev]"
```

## Step 1: start the AI service

In a terminal that you leave open:

```bash
cd ~/PycharmProjects/TrendAssessmentModel
.venv/bin/python -m trend_assessment
```

It is ready when it prints:

```text
INFO:     Uvicorn running on http://127.0.0.1:8787 (Press CTRL+C to quit)
```

This terminal then shows one line per assessment, which is useful to watch while testing.

## Step 2: check the service without cTrader

In a second terminal. If this step fails, the cBot cannot work either, so fix it here first.

Load the model:

```bash
curl -s -X POST http://127.0.0.1:8787/v1/warmup
```

```json
{"model":"gemma3:27b","loaded":true,"load_ms":9644}
```

`load_ms` is about 10000 the first time and close to 0 once the model is in memory.

Send it a chart picture. Any PNG screenshot of the chart will do, for example one from
`~/Documents/TakeChartshot`:

```bash
(printf '{"request_id":"test-1","image_png_base64":"'; base64 -i ~/Documents/TakeChartshot/2.png | tr -d '\n'; printf '"}') \
  | curl -s http://127.0.0.1:8787/v1/assessments -H 'Content-Type: application/json' --data-binary @- \
  | python3 -m json.tool
```

```json
{
    "request_id": "test-1",
    "status": "OK",
    "trend": "UP",
    "confidence": 0.85,
    "daily_vwap_direction": "RISING",
    "daily_vwap_confidence": 0.9,
    "structure": "Uptrend with recent consolidation",
    "reason": "The price action shows generally higher highs and higher lows, indicating an uptrend. The solid yellow Daily VWAP line has a clear upward slope, confirming the rising trend.",
    "model": "gemma3:27b",
    "elapsed_ms": 5735
}
```

Open the picture yourself and compare. This is the quickest way to see what the model makes of a
chart: you do not have to wait for a signal.

## Step 3: build the cBot and load it

```bash
cd ~/cAlgo/Sources/Robots/VWAPTradeAI
dotnet build "VWAPTradeAI.sln"
```

Then refresh the cBot in cTrader so it picks up the new build.

## Step 4: set up the chart

Use a **demo account** for the first runs.

- Open an **M5** chart of the symbol and add a VWAPTradeAI instance to it.
- **Keep the chart visible.** cTrader can only take a picture of a chart that is on screen. A
  chart in a background tab, or a minimised cTrader, means every signal is rejected.
- **Scroll to the latest bar and leave the zoom alone.** The model is shown exactly what you see.
  If you scroll back in history, it judges that history.

Parameters for a test run:

| Parameter | Value for testing | Why |
| --- | --- | --- |
| `风险%` | something small, e.g. 0.1 | It is a test |
| `启用AI趋势过滤` | **true** | Turns the filter on |
| `AI服务地址` | `http://127.0.0.1:8787` | The default |
| `AI超时秒数` | 30 | The default |
| `保存AI评估截图` | **true** | Keeps every picture and answer so you can review them |

`启用AI趋势过滤` and `保存AI评估截图` are off by default, and cTrader keeps the saved values of an
existing instance, so check both on the instance you start.

## Step 5: start it and read the start-up log

In the cBot's Log tab the bot's own lines start with `****`. Among the start-up lines you should
see these, in this order:

```text
Trade settings | RiskPct: 0.1, TakeProfitR: 2, StopOffsetTicks: 50
AI trend filter on | Service: http://127.0.0.1:8787, TimeoutSeconds: 30, Recording: True
AI assessment folder: /Users/<you>/Documents/TrendAssessment
VWAP break and reverse started.
AI service ready | The model is loaded
```

`AI service ready` arrives a moment after the others, because the bot does not wait for it.

If you see this instead, the service is not reachable; go back to Step 1:

```text
AI service warm-up failed | Reason: The AI service is not reachable at http://127.0.0.1:8787/: ... | Signals are rejected until the service answers
```

The bot keeps running in that case. Once the service is up, the next signal is assessed normally;
there is no need to restart the bot.

## Step 6: what happens at a signal

A signal needs a Strong bar and a candle pattern touching the daily VWAP, so on one M5 chart you
may wait a while for one. When it comes, the log shows, in this order:

```text
AI trend requested | Signal: Buy L_Pin_1 | RequestId: 7f3c2a9e5d414b0fa1c6e2b8d4a90c13
```

The marker appears on the chart at the same moment. Some seconds later, one of these:

```text
AI trend accepted | Signal: Buy | Trend: UP | TrendConfidence: 0.85 | DailyVWAP: RISING | DailyVWAPConfidence: 0.9 | ElapsedMs: 6120
```

```text
AI trend rejected | Signal: Buy | Trend: UP | TrendConfidence: 0.93 | DailyVWAP: FLAT | DailyVWAPConfidence: 0.95 | Rejected: Daily VWAP is FLAT | Reason: <the model's explanation> | ElapsedMs: 5980
```

```text
AI trend unreadable | Signal: Buy | Reason: <what the picture lacks> | ElapsedMs: 4680 | Trade rejected
```

```text
AI trend unavailable | Signal: Sell | Reason: Local assessment request timed out after 30 seconds | ElapsedMs: 30004 | Trade rejected
```

After `AI trend accepted` the usual order lines follow: `Order plan | ...` and
`Order submitted | ...`, or `Order not placed | ...` with the reason. An accepted signal can still
be stopped there, for example when a position is already open on the level.

With recording on, each assessment ends with:

```text
AI assessment recorded | /Users/<you>/Documents/TrendAssessment/20260930-220500_XAUUSD_7f3c2a9e.png
```

The service's terminal shows the same assessment from its side:

```text
trend_assessment.api | assessment | request_id=7f3c2a9e5d414b0fa1c6e2b8d4a90c13 model=gemma3:27b status=OK trend=UP daily_vwap=RISING elapsed_ms=6050
```

## Step 7: what to check

These are the behaviours worth confirming. The first three are the ones that have never been run
inside cTrader, so check them first.

| # | Check | How | You should see |
| --- | --- | --- | --- |
| 1 | The AI's picture has no marker for its own signal | Open the newest `.png` in `~/Documents/TrendAssessment` | The chart up to the latest bars. Markers of earlier signals may be there; the marker of this signal must not be |
| 2 | The picture shows the right moment | Same picture | The latest bars, with the signal bar among the last two on the right |
| 3 | The order follows the answer | An accepted signal | `AI trend accepted`, then `Order submitted`, a few seconds after the bar opened. The cBot stays responsive meanwhile |
| 4 | A rejected signal does not trade | A rejected signal | `AI trend rejected`, no order, and the marker stays on the chart |
| 5 | The decision matches the rule | Compare the log line with the table below | Buy passes only with `UP`, Sell only with `DOWN`, and never with `FLAT` |
| 6 | No service means no trade | Stop the service (Ctrl+C) and wait for a signal, or start the bot with the service stopped | `AI trend unavailable ... Trade rejected`, or at start-up `AI service warm-up failed`. The bot keeps running |
| 7 | A hidden chart means no trade | Switch to another chart tab and wait for a signal | `AI trend unavailable ... The chart is not visible ... Trade rejected` |
| 8 | Backtests refuse the filter | Start a backtest with `启用AI趋势过滤` on | `参数有误，已停止：AI趋势过滤只支持实盘和模拟盘` and the bot stops |
| 9 | Off means unchanged | Run a backtest with `启用AI趋势过滤` off | It trades as it always did; no `AI trend` lines at all |

The rule for check 5:

| Signal | Passes when |
| --- | --- |
| Buy | trend `UP` and daily VWAP `RISING` or `FALLING` |
| Sell | trend `DOWN` and daily VWAP `RISING` or `FALLING` |

Everything else is rejected: `SIDEWAYS`, a trend against the signal, a `FLAT` daily VWAP, an
unreadable picture, and any failure.

## Reading the recordings

Each assessment leaves two files in `~/Documents/TrendAssessment`, named by the signal bar's time
and the symbol. The `.png` is exactly what the model was sent. The `.json` says what came of it:

| Field | Meaning |
| --- | --- |
| `signal_side`, `signal_label`, `signal_bar_time` | The signal |
| `outcome` | `OK`, `UNREADABLE`, or `UNAVAILABLE` (no valid answer) |
| `trend`, `daily_vwap_direction`, `structure`, `reason` | What the model said |
| `confidence`, `daily_vwap_confidence` | The model's own estimates. Not used for trading |
| `gate`, `gate_reject_reason` | `PASS` or `REJECT`, and why |
| `order_placed`, `order_reject_reason` | Whether an order went out after a `PASS`, and if not, why |
| `elapsed_ms` | How long the answer took |

This folder is never cleared by the bot. It is the material for judging the model: look at a
picture, decide what you would have answered, and compare. When the model is wrong, follow
[Correcting the model when it is wrong](https://github.com/richardgong1987/TrendAssessmentModel/blob/main/docs/CORRECTING_THE_MODEL.md)
(`docs/CORRECTING_THE_MODEL.md` in the TrendAssessmentModel project).

## When something goes wrong

| You see | Cause | What to do |
| --- | --- | --- |
| `AI service warm-up failed ... not reachable` | The service is not running | Step 1 |
| `AI trend unavailable ... not reachable` | The service stopped after start-up | Step 1; no need to restart the bot |
| `AI trend unavailable ... The chart is not visible` | The chart is in a background tab or cTrader is minimised | Bring the chart to the front |
| `AI trend unavailable ... HTTP 504 model_timeout` | Ollama took longer than the service's 25 seconds: the model was not loaded, or another request was ahead of it | Run the warm-up from Step 2. With several bot instances, see the note below |
| `AI trend unavailable ... timed out after 30 seconds` | The service accepted the request and never answered | Look at the service's terminal; restart the service |
| `AI trend unavailable ... HTTP 502 model_unavailable` | Ollama is not running, or the model is missing | Start Ollama; `ollama list` |
| `AI trend unreadable` | The model saw no candles or no solid yellow daily VWAP line | Look at the recorded picture: is the chart empty, scrolled away, or the VWAP off screen? |
| `AI trend rejected ... Signal expired` | The answer came after the next bar had closed | Should not happen with a 30 second timeout; check the machine was not asleep |
| `参数有误，已停止：AI趋势过滤只支持实盘和模拟盘` | The filter is on in a backtest or optimization | Turn `启用AI趋势过滤` off for backtests |
| `参数有误，已停止：AI服务地址无效` | The address is not an `http://` URL | Use `http://127.0.0.1:8787` |
| No `AI trend` lines at all | No signal yet, or the filter is off on this instance | Check the start-up log for `AI trend filter on` |
| The model's answer looks wrong | The model, not the plumbing | See the correcting guide linked above |

**Several instances at once.** Ollama assesses one picture at a time. If two instances signal on
the same bar, the second waits for the first and can run past the limits. To allow for it, start
the service with a longer limit and raise the bot's to stay above it:

```bash
MODEL_TIMEOUT_SECONDS=55 .venv/bin/python -m trend_assessment     # and set AI超时秒数 to 60
```

## Stopping

1. Stop the cBot in cTrader.
2. Press Ctrl+C in the service's terminal.
3. The model stays in memory (about 18.6 GB) until Ollama restarts. To free it now:

```bash
ollama stop gemma3:27b
```
