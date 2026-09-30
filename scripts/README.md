# Batch backtest script (run_conditions.py)

Runs cTrader historical backtests (`backtest`) one record at a time, following the plan the
backend parameter API hands down. Each run finishes and automatically moves on to the next, and
each writes its own backtest report JSON.

## Installing dependencies

```bash
pip install -r scripts/requirements.txt
```

Third-party dependencies: `pandas` + `matplotlib` (summarize backtest reports and draw the bar
chart) and `python-dotenv` (parse `.env`); everything else is the Python standard library — the
parameter API is fetched with `urllib`.

## Environment config (.env)

Account, paths, credentials and other environment-specific settings live in a `.env` file
instead of being hard-coded, so you don't have to copy the script per environment — just
maintain the env file:

```bash
cp scripts/.env.example scripts/.env        # first time: create the dev config and fill it in
```

Required keys: `AUTH_TOKEN`, `CTRADER_BIN`, `CTID`, `ACCOUNT`,
`CTRADER_PARAMETER_RECORDS_URL` (the backtest plan source — without it there is nothing to run);
optional: `DATA_MODE` (default `m1`), `BALANCE` (default `10000`),
`REPORT_UPLOAD_URL` (blank = skip the report upload).

The `.algo` path is not configured: `dotnet build` publishes the package to the directory
above the repo root, named after the repo root folder, so `backtest/config.py` derives it.
Build first — the run aborts with a clear error if the `.algo` is missing.

### Inheritance: `.env` is the shared base

`scripts/.env` is loaded for **every** environment. An environment-specific file passed via
`--env-file` (e.g. `.env-prod`) is loaded on top of it and only needs the keys that differ —
same-named keys win, everything else is inherited:

```
scripts/.env         AUTH_TOKEN, CTRADER_BIN, CTID, ACCOUNT, ...   # shared, edit once
scripts/.env-prod    only the keys that differ in production
```

So the required-keys check applies to the merged result: a key that lives in `.env` does not
have to be repeated in `.env-prod`. Note the flip side — a key you *delete* from `.env-prod`
falls back to the base value rather than becoming unset; to blank one out, write `KEY=`.

`.env` and `.env-prod` contain the auth token and are ignored in `.gitignore`, so they are
never committed; only the `.env.example` template is version-controlled.

## How to use

1. Add/remove parameter records in the backend admin (they are what this script fetches).
2. Run:

   ```bash
   python3 scripts/run_conditions.py                          # reads scripts/.env by default
   python3 scripts/run_conditions.py --env-file scripts/.env-prod   # .env + prod overrides
   python3 scripts/run_conditions.py --jobs 4                 # run up to 4 at a time (opt-in)
   ```

3. Each run's report JSON is written to `~/Documents/trading_reports/`.

Before running, the script validates inputs (missing env file / missing required keys, API
error responses, records missing `symbol`/`period`/the fields the report filename needs,
invalid date format) and fails with a clear message naming the offending `recordId`, so it
never runs with a broken config.

### Parallel backtests (--jobs)

`--jobs N` controls how many backtests run at once. **The default is 1 (sequential).**
Running several cTrader processes at once has been observed to make backtests fail
intermittently inside cTrader's own report-saving step
(`InvalidOperationException: Message expected`), and a failed task disappears from the summary
without stopping the batch — so parallelism is opt-in. If you do use `--jobs N`, check that the
number of report JSONs matches the number of plan rows. Backtesting is
CPU/memory intensive — going beyond the physical core count usually isn't faster and just
makes the runs contend for resources.

The first time you run a given symbol in parallel, it's best to run one with `--jobs 1` first
to warm the m1 data cache, then scale up — this avoids multiple processes downloading the
same data at once and conflicting. The summary chart `final_report.png` is generated once
after all tasks finish, so concurrency doesn't affect it.

> cTrader's official docs state the backtesting engine supports running multiple backtest
> processes in parallel, but concurrency is not explicitly endorsed at the CLI level. For a
> first parallel run, validate a small sample (2–3 rows) produces correct reports before
> scaling up.

## Backtest plan (parameter API)

`GET $CTRADER_PARAMETER_RECORDS_URL` returns the plan; **one record = one backtest**:

```json
{"code": 200, "success": true, "data": [
  {"recordId": 1, "symbol": "XAUUSD", "period": "m5",
   "parameterFields": [
     {"name": "TakeProfitR", "type": "double", "value": 2},
     {"name": "RiskPct",     "type": "double", "value": 1},
     {"name": "start",       "type": "date",   "value": "2026-01-01"}
   ]}
]}
```

- `symbol` / `period` become `--symbol` / `--period`.
- Every entry in `parameterFields` becomes `--<name>=<value>` verbatim, so **`name` must match
  the cBot's C# property name exactly** (the cTrader CLI matches by property name, not by the
  Chinese display name). Adding or removing a backtest parameter is a backend-record change
  only — this script keeps no parameter whitelist.
- `value` is formatted by `type`: `double` → `2.0` becomes `2`, `1.75` kept as is; `int`/`enum`
  → integer; `bool` → `True`/`False`; `date` → the `YYYY-MM-DD` from the API is converted to the
  **`DD/MM/YYYY` (UTC)** cTrader expects.
- A field with an empty value is **not passed**, so the cBot falls back to its own default.

## Report filename

Each backtest writes a **backtest report JSON** (`--report-json`) named by joining the record's
distinguishing fields (dates use the compact `YYYYMMDD` form):

```
<symbol>-<period>-<take-profit>-<start-date>-<end-date>.json
```

Example: `~/Documents/trading_reports/XAUUSD-h1-2-20260601-20260630.json`.

Two records that differ only in a parameter outside the filename (e.g. `RiskPct`) overwrite
each other's report; the end-of-batch summary names the resulting gap.

## Summary outputs (final_report.png + final_summary_report.csv)

Once **all** tasks finish, the script scans **all** backtest report JSONs in the output
directory, summarizes them with `pandas`, and writes two files to `~/Documents/trading_reports/`:

**1. `final_report.png`** — two stacked bar charts drawn with `matplotlib`:

- **Top chart**: net profit per report (`main.netProfit`), green for profit, red for loss.
- **Bottom chart**: win rate per report (`winningTrades.all / totalTrades.all`).
- The X-axis label is each report's full filename (without extension), so you can tell at a
  glance which parameter set / date range it is.

**2. `final_summary_report.csv`** — one row per report, columns:

```
文件名, 起始日期, 结束日期, 周期, 止盈目标, 胜率%, 盈利金额, 盈利率%
XAUUSD-m5-2-20240101-20240131, 20240101, 20240131, m5, 2R, 29%, -509$, -5.09%
```

`胜率%` (win rate), `盈利金额` (net profit) and `盈利率%` (return on capital) come from the
report JSON — win rate = `winningTrades.all / totalTrades.all`, and `盈利率% = netProfit /
startingCapital × 100` (starting capital read from each report, `10000` by default). The rest
(filename, start/end dates, period, take-profit) are parsed straight from the report filename.
Written with a UTF-8 BOM so the Chinese headers open correctly in Excel.

Both are generated once at the end of the batch. The summary logic lives in the `summary/`
package; `report_summary.py` is a thin CLI over it that can be run standalone to (re)generate
both files manually at any time — e.g. mid-run in another terminal, or without re-running
backtests:

```bash
python3 scripts/report_summary.py                 # scans ~/Documents/trading_reports by default
python3 scripts/report_summary.py --dir <dir>     # specify the report directory
```

> Note: the summary covers **all** report JSONs in the directory, including leftovers from
> previous runs. To summarize only one batch, clear the old `*.json` from the directory first.

## Tunables

Environment-related (edit in `.env` / `.env-prod`):

- `AUTH_TOKEN` / `CTRADER_BIN` / `CTID` / `ACCOUNT` (account, paths, credentials)
- `CTRADER_PARAMETER_RECORDS_URL` (parameter API — the backtest plan source)
- `REPORT_UPLOAD_URL` (where the report zip is uploaded; blank = skip)
- `BALANCE` (starting capital, default `10000`)
- `DATA_MODE` (backtest data mode, default `m1`; options `open`, `m1-csv`)

Strategy parameters: **all of them come from the parameter API** — the script no longer pins
any cBot parameter. To change one, edit the backend record; to add or remove one, add or remove
a `parameterFields` entry (its `name` must be the cBot's C# property name). A parameter the API
doesn't send falls back to the cBot's own default.

## Code structure

The CLI entry point `run_conditions.py` only does the "composition" (parse args + wire the
modules together); the actual logic is split by responsibility into the `backtest/` package,
each part doing one thing and decoupled from the others:

```
run_conditions.py     Backtest CLI entry point (composition root: parse_args + main)
report_summary.py     Chart CLI entry point (refresh final_report.png standalone)
backtest/             Running backtests
  config.py           read .env, produce Config (account/paths/credentials/API url/capital)
  records.py          GET the parameter API -> raw parameter records                 [HTTP]
  parameters.py       a parameterField -> a CLI argument (value formatting by type)  [data]
  plan.py             parameter record -> backtest task (ConditionRow)
  command.py          task + config -> cTrader CLI command
  runner.py           run tasks sequentially/in parallel; generate the chart once at the end
summary/              Summarizing results
  metrics.py          read report JSONs -> DataFrame (win rate / net profit)   [data]
  naming.py           parse a report filename -> its fields (symbol/period/…)  [data]
  chart.py            DataFrame -> two-panel bar chart PNG                       [presentation]
  table.py            DataFrame -> final_summary_report.csv                      [presentation]
  report.py           scan dir -> summarize -> chart + csv (public: update_final_report)
```

Dependency direction: `run_conditions → {backtest.plan, backtest.runner}`,
`backtest.plan → {backtest.records, backtest.parameters}`,
`backtest.runner → {backtest.command, summary}`, and `report_summary → summary`.
Within `summary`: `report → {metrics, chart, table}` and `table → naming`. Leaf modules
(`config` / `records` / `parameters` / `command` / `metrics` / `naming` / `chart` / `table`)
don't depend back on their orchestrators.

## Key design notes

- Uses the CLI's **`backtest`** subcommand (historical backtest, stops when done), **not
  `run`** (`run` is live/forward execution, stays connected to the live account and never
  exits on its own).
- The command includes **`--exit-on-stop`**: after a backtest finishes the process would not
  exit on its own (it idles); this flag makes it terminate so the script can move to the next.
- Dates are always passed as **DD/MM/YYYY (UTC)** to cTrader; the script validates the format.

Official CLI docs: <https://help.ctrader.com/ctrader-algo/documentation/ctrader-cli/>
