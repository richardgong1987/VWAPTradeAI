using System;
using System.Diagnostics;

namespace cAlgo.Robots;

// The picture of the chart the AI is shown for a signal. It must show the signal's own bar, which
// has been missed before: in a fast visual backtest the chart is drawn behind the cBot, and pictures
// taken at once ended one bar to an hour and a half before the signal. The signal's marker is
// already drawn when OnSignal is called, so the picture shows it.
//
// So the picture is only taken once the chart shows the bar after the signal's (which means the
// signal's bar is complete on screen):
//
//   OnSignal   look at once
//   OnTick     still not shown: look again on every tick of the same bar; after two ticks scroll
//              the chart to the newest bar once, in case it was scrolled back
//   OnNewBar   give up on a signal whose bar the chart never showed
//
// A visual backtest needs more. Its chart is drawn behind the cBot and only moves on between the
// cBot's handlers: holding the cBot thread inside OnBar (1 s, later 2 s) never let it catch up. And
// its market time races: 60 simulated seconds have passed in 40 ms. So in a backtest each look that
// finds the chart behind ends with a 100 ms pause, which slows the backtest during this one bar and
// gives the chart real time before the next tick looks again; the wait ends after 50 pauses (5 s
// of real time), not after a minute of market time.
//
// Whatever happens, onPicture is called once per signal: with the picture, or with null after
// giving up, which the AI filter turns into a logged rejection.
//
// Which of these steps a run needed is logged, because cTrader does not document how its chart
// keeps up in a backtest.
//
// No cAlgo dependency: the chart comes in as IChartCamera, the clocks and the pause as functions.
public class AiChartshots {
    private static readonly TimeSpan BacktestPause = TimeSpan.FromMilliseconds(100);
    private const int MaxBacktestPauses = 50;

    // Live only. Ticks keep coming, and a chart that has not caught up within a minute will not.
    private static readonly TimeSpan LiveWaitLimit = TimeSpan.FromSeconds(60);

    private const int TicksBeforeScroll = 2;

    private readonly IChartCamera _camera;
    private readonly bool _isBacktest;
    private readonly Action<TimeSpan> _pause;
    private readonly Func<DateTime> _marketTime;
    private readonly Action<SignalModel, byte[]> _onPicture;
    private readonly Action<string> _log;

    private SignalModel _pending;
    private DateTime _giveUpAt;
    private Stopwatch _waited;
    private int _ticksWaited;
    private int _pauses;
    private bool _hasScrolled;

    // isBacktest: holding the cBot thread stops the backtest's clock instead of missing ticks, so a
    // backtest may pause while it waits; live never does.
    public AiChartshots(IChartCamera camera, bool isBacktest, Action<TimeSpan> pause, Func<DateTime> marketTime,
        Action<SignalModel, byte[]> onPicture, Action<string> log) {
        _camera = camera;
        _isBacktest = isBacktest;
        _pause = pause;
        _marketTime = marketTime;
        _onPicture = onPicture;
        _log = log;
    }

    public void OnSignal(SignalModel signal) {
        // OnNewBar has normally settled the last one already; never drop a signal unanswered.
        OnNewBar();

        _pending = signal;
        _giveUpAt = _marketTime() + LiveWaitLimit;
        _waited = Stopwatch.StartNew();
        _ticksWaited = 0;
        _pauses = 0;
        _hasScrolled = false;

        TryTake();
    }

    public void OnTick() {
        if (_pending == null)
            return;

        _ticksWaited++;
        TryTake();
    }

    // Call before looking for a new signal: a signal is only ever pictured during the bar after its own.
    public void OnNewBar() {
        if (_pending != null)
            GiveUp("a new bar opened before the chart showed the signal's bar");
    }

    private void TryTake() {
        if (!_camera.IsVisible) {
            GiveUp("the chart is not visible");
            return;
        }

        if (!ShowsSignalBar() && !_hasScrolled && _ticksWaited >= TicksBeforeScroll)
            ScrollToNewestBar();

        if (ShowsSignalBar()) {
            Take();
            return;
        }

        if (_isBacktest)
            PauseBeforeNextTick();
        else if (_marketTime() >= _giveUpAt)
            GiveUp($"the chart did not show the signal's bar within {LiveWaitLimit.TotalSeconds:0} seconds");
    }

    // The last thing the handler does: the chart catches up while the backtest stands still here,
    // and the cBot sees it from the next tick on.
    private void PauseBeforeNextTick() {
        if (_pauses >= MaxBacktestPauses) {
            GiveUp($"the chart did not show the signal's bar within {MaxBacktestPauses * BacktestPause.TotalSeconds:0} seconds " +
                   "of backtest pauses");
            return;
        }

        _pauses++;
        _pause(BacktestPause);
    }

    private bool ShowsSignalBar() {
        return _camera.LastVisibleBarIndex > _pending.BarIndex;
    }

    private void ScrollToNewestBar() {
        int before = _camera.LastVisibleBarIndex;
        _camera.ScrollXTo(_pending.BarIndex + 1);
        _hasScrolled = true;
        _log($"AI chart scrolled | SignalBar: {_pending.BarIndex} | LastVisibleBar: {before} -> {_camera.LastVisibleBarIndex} | " +
             $"FirstVisibleBar: {_camera.FirstVisibleBarIndex}");
    }

    private void Take() {
        byte[] picture = _camera.TakeChartshot();
        _log($"AI chart picture | SignalBar: {_pending.BarIndex} | LastVisibleBar: {_camera.LastVisibleBarIndex} | " +
             $"FirstVisibleBar: {_camera.FirstVisibleBarIndex} | Waited: {_waited.ElapsedMilliseconds} ms, {_ticksWaited} ticks");
        Finish(picture);
    }

    private void GiveUp(string reason) {
        _log($"AI chart not current | Signal: {_pending.Level.Side} {_pending.Label} | Reason: {reason} | SignalBar: {_pending.BarIndex} | " +
             $"LastVisibleBar: {_camera.LastVisibleBarIndex} | Waited: {_waited.ElapsedMilliseconds} ms, {_ticksWaited} ticks");
        Finish(null);
    }

    private void Finish(byte[] picture) {
        SignalModel signal = _pending;
        _pending = null;
        _onPicture(signal, picture);
    }
}
