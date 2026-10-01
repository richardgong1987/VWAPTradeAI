using System;
using System.Diagnostics;

namespace cAlgo.Robots;

// The picture of the chart the AI is shown for a signal. It must show the signal's own bar, and it
// must be taken before that signal's marker is drawn. Both have been missed before: in a fast visual
// backtest the chart is drawn behind the cBot, and pictures taken at once ended one bar to an hour
// and a half before the signal.
//
// So the picture is only taken once the chart shows the bar after the signal's (which means the
// signal's bar is complete on screen):
//
//   OnSignal   backtest: wait here, up to 2 s, holding the cBot thread so the clock stands still
//   OnTick     still not shown: check again on every tick of the same bar; after two ticks scroll
//              the chart to the newest bar once, in case it was scrolled back
//   OnNewBar   give up on a signal whose bar the chart never showed
//
// Whatever happens, onPicture is called once per signal: with the picture, or with null after
// giving up, which the AI filter turns into a logged rejection. The signal's marker is drawn by
// onPicture, so always after the picture.
//
// Which of these steps a run needed is logged, because cTrader does not document how its chart
// keeps up in a backtest.
//
// No cAlgo dependency: the chart comes in as IChartCamera, the clocks and the pause as functions.
public class AiChartshots {
    // Backtest: look every 100 ms, for up to 2 s.
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);
    private const int MaxHoldPolls = 20;

    // Market time, so in a backtest it counts simulated seconds. Live, ticks keep coming, and a
    // chart that has not caught up within a minute will not.
    private static readonly TimeSpan TickWaitLimit = TimeSpan.FromSeconds(60);

    private const int TicksBeforeScroll = 2;

    private readonly IChartCamera _camera;
    private readonly bool _mayHoldThread;
    private readonly Action<TimeSpan> _pause;
    private readonly Func<DateTime> _marketTime;
    private readonly Action<SignalModel, byte[]> _onPicture;
    private readonly Action<string> _log;

    private SignalModel _pending;
    private DateTime _giveUpAt;
    private Stopwatch _waited;
    private int _ticksWaited;
    private bool _hasScrolled;

    // mayHoldThread: a backtest, where holding the cBot thread stops the clock instead of missing it.
    public AiChartshots(IChartCamera camera, bool mayHoldThread, Action<TimeSpan> pause, Func<DateTime> marketTime,
        Action<SignalModel, byte[]> onPicture, Action<string> log) {
        _camera = camera;
        _mayHoldThread = mayHoldThread;
        _pause = pause;
        _marketTime = marketTime;
        _onPicture = onPicture;
        _log = log;
    }

    public void OnSignal(SignalModel signal) {
        // OnNewBar has normally settled the last one already; never drop a signal unanswered.
        OnNewBar();

        _pending = signal;
        _giveUpAt = _marketTime() + TickWaitLimit;
        _waited = Stopwatch.StartNew();
        _ticksWaited = 0;
        _hasScrolled = false;

        if (_mayHoldThread)
            HoldUntilShown();

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

    private void HoldUntilShown() {
        for (int poll = 0; poll < MaxHoldPolls && _camera.IsVisible && !ShowsSignalBar(); poll++)
            _pause(PollInterval);
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

        if (_marketTime() >= _giveUpAt)
            GiveUp($"the chart did not show the signal's bar within {TickWaitLimit.TotalSeconds:0} seconds");
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
