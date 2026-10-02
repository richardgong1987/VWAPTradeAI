using System;
using System.Collections.Generic;
using cAlgo.Robots;
using Xunit;

namespace VWAPTradeAI.Tests.Chartshots {
    // When the AI's picture is taken. It must show the signal's own bar (the chart can lag behind
    // the cBot, or be scrolled back), and every signal must be handed on exactly once, with a
    // picture or with null.
    public class AiChartshotsTests {
        private static readonly byte[] Picture = { 0x89, 0x50, 0x4E, 0x47 };

        private readonly SignalModel _signal = TestSignal.Long(close: 100.0, stopLoss: 98.0); // on bar 42
        private readonly FakeChartCamera _camera = new() { LastVisibleBarIndex = 43 };
        private readonly List<(SignalModel Signal, byte[] Picture)> _handedOn = new();
        private readonly List<string> _log = new();
        private int _pauses;
        private DateTime _now = new(2026, 8, 4, 7, 5, 0);

        private AiChartshots Chartshots(bool isBacktest) {
            return new AiChartshots(_camera, isBacktest, _ => _pauses++, () => _now, (signal, picture) => _handedOn.Add((signal, picture)),
                _log.Add);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void a_chart_that_already_shows_the_signals_bar_is_pictured_at_once(bool isBacktest) {
            Chartshots(isBacktest).OnSignal(_signal);

            (SignalModel signal, byte[] picture) = Assert.Single(_handedOn);
            Assert.Same(_signal, signal);
            Assert.Same(Picture, picture);
            Assert.Equal(0, _pauses);
            Assert.Empty(_camera.Scrolls);
        }

        [Fact]
        public void the_chart_must_show_the_bar_after_the_signals_so_the_signals_bar_is_complete() {
            _camera.LastVisibleBarIndex = 42; // the signal's bar is the newest one drawn: still forming on screen

            Chartshots(isBacktest: false).OnSignal(_signal);

            Assert.Empty(_handedOn);
        }

        [Fact]
        public void in_a_backtest_each_look_that_finds_the_chart_behind_pauses_before_the_next_tick() {
            _camera.LastVisibleBarIndex = 41;
            AiChartshots chartshots = Chartshots(isBacktest: true);

            chartshots.OnSignal(_signal);
            Assert.Equal(1, _pauses);

            chartshots.OnTick();
            Assert.Equal(2, _pauses);
            Assert.Empty(_handedOn);

            _camera.LastVisibleBarIndex = 43; // the chart caught up between ticks, as it does in cTrader
            chartshots.OnTick();

            Assert.Equal(2, _pauses);
            Assert.Same(Picture, Assert.Single(_handedOn).Picture);
            Assert.Contains(_log, line => line.StartsWith("AI chart picture | SignalBar: 42 | LastVisibleBar: 43") && line.EndsWith("2 ticks"));
        }

        [Fact]
        public void in_a_backtest_a_minute_of_market_time_does_not_end_the_wait() {
            _camera.LastVisibleBarIndex = 41;
            AiChartshots chartshots = Chartshots(isBacktest: true);
            chartshots.OnSignal(_signal);

            _now = _now.AddSeconds(90); // a fast backtest gets through this in a few milliseconds
            chartshots.OnTick();

            Assert.Empty(_handedOn);
        }

        [Fact]
        public void in_a_backtest_the_wait_ends_after_fifty_pauses_without_a_picture() {
            _camera.LastVisibleBarIndex = 41;
            _camera.ScrollMoves = false; // the chart is at the newest bar it has, so scrolling cannot help
            AiChartshots chartshots = Chartshots(isBacktest: true);
            chartshots.OnSignal(_signal);

            for (int tick = 0; tick < 49; tick++)
                chartshots.OnTick();

            Assert.Equal(50, _pauses); // 50 × 100 ms
            Assert.Empty(_handedOn);

            chartshots.OnTick();

            Assert.Equal(50, _pauses);
            Assert.Null(Assert.Single(_handedOn).Picture);
            Assert.Contains(_log, line => line.Contains("Reason: the chart did not show the signal's bar within 5 seconds of backtest pauses"));
        }

        [Fact]
        public void live_the_cbot_never_pauses() {
            _camera.LastVisibleBarIndex = 40;
            AiChartshots chartshots = Chartshots(isBacktest: false);

            chartshots.OnSignal(_signal);
            _camera.LastVisibleBarIndex = 43;
            chartshots.OnTick();

            Assert.Equal(0, _pauses);
            Assert.Single(_handedOn);
        }

        [Fact]
        public void a_chart_scrolled_back_is_scrolled_to_the_newest_bar_after_two_ticks() {
            _camera.LastVisibleBarIndex = 30;
            AiChartshots chartshots = Chartshots(isBacktest: false);

            chartshots.OnSignal(_signal);
            chartshots.OnTick();
            Assert.Empty(_camera.Scrolls);

            chartshots.OnTick();

            Assert.Equal(new[] { 43 }, _camera.Scrolls);
            Assert.Same(Picture, Assert.Single(_handedOn).Picture);
            Assert.Contains(_log, line => line.StartsWith("AI chart scrolled | SignalBar: 42 | LastVisibleBar: 30 -> 43"));
        }

        [Fact]
        public void the_chart_is_scrolled_only_once() {
            _camera.LastVisibleBarIndex = 30;
            _camera.ScrollMoves = false; // a scroll that does not bring the bar into view
            AiChartshots chartshots = Chartshots(isBacktest: false);

            chartshots.OnSignal(_signal);

            for (int tick = 0; tick < 5; tick++)
                chartshots.OnTick();

            Assert.Single(_camera.Scrolls);
            Assert.Empty(_handedOn);
        }

        [Fact]
        public void live_after_a_minute_of_market_time_without_the_bar_the_signal_is_handed_on_without_a_picture() {
            _camera.LastVisibleBarIndex = 30;
            _camera.ScrollMoves = false;
            AiChartshots chartshots = Chartshots(isBacktest: false);
            chartshots.OnSignal(_signal);
            chartshots.OnTick();

            _now = _now.AddSeconds(60);
            chartshots.OnTick();

            (SignalModel signal, byte[] picture) = Assert.Single(_handedOn);
            Assert.Same(_signal, signal);
            Assert.Null(picture);
            Assert.Contains(_log, line => line.StartsWith("AI chart not current | Signal: Buy L_Pin_1 | " +
                                                          "Reason: the chart did not show the signal's bar within 60 seconds"));
        }

        [Fact]
        public void a_signal_still_waiting_when_the_next_bar_opens_is_handed_on_without_a_picture() {
            _camera.LastVisibleBarIndex = 40;
            AiChartshots chartshots = Chartshots(isBacktest: false);
            chartshots.OnSignal(_signal);

            chartshots.OnNewBar();

            Assert.Null(Assert.Single(_handedOn).Picture);
            Assert.Contains(_log, line => line.Contains("Reason: a new bar opened before the chart showed the signal's bar"));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void a_hidden_chart_hands_the_signal_on_at_once_without_a_picture(bool isBacktest) {
            _camera.IsVisible = false;

            Chartshots(isBacktest).OnSignal(_signal);

            Assert.Null(Assert.Single(_handedOn).Picture);
            Assert.Equal(0, _pauses);
            Assert.Equal(0, _camera.PicturesTaken);
            Assert.Contains(_log, line => line.Contains("Reason: the chart is not visible"));
        }

        [Fact]
        public void every_signal_is_handed_on_exactly_once() {
            AiChartshots chartshots = Chartshots(isBacktest: false);
            chartshots.OnSignal(_signal);

            chartshots.OnTick();
            chartshots.OnTick();
            chartshots.OnNewBar();

            Assert.Single(_handedOn);
        }

        [Fact]
        public void a_new_signal_settles_one_still_waiting() {
            _camera.LastVisibleBarIndex = 40;
            AiChartshots chartshots = Chartshots(isBacktest: false);
            SignalModel later = TestSignal.Short(close: 100.0, stopLoss: 102.0);
            chartshots.OnSignal(_signal);

            chartshots.OnSignal(later);

            (SignalModel signal, byte[] picture) = Assert.Single(_handedOn);
            Assert.Same(_signal, signal);
            Assert.Null(picture);
        }

        // Stands in for cTrader's chart: which bars it shows, and what scrolling does to that.
        private sealed class FakeChartCamera : IChartCamera {
            public int PicturesTaken { get; private set; }

            public bool IsVisible { get; set; } = true;

            public int FirstVisibleBarIndex { get; set; }

            public int LastVisibleBarIndex { get; set; }

            // False: scrolling leaves the view where it was.
            public bool ScrollMoves { get; set; } = true;

            public List<int> Scrolls { get; } = new();

            public void ScrollXTo(int barIndex) {
                Scrolls.Add(barIndex);

                if (ScrollMoves)
                    LastVisibleBarIndex = barIndex;
            }

            public byte[] TakeChartshot() {
                PicturesTaken++;
                return IsVisible ? Picture : null;
            }
        }
    }
}
