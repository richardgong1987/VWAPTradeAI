using cAlgo.Robots;
using Xunit;

namespace DayTradeSelf.Tests.Vwap {
    // VWAP = Σ(hlc3 × volume) / Σ(volume), restarted on each new period. The calculator is fed one
    // bar at a time and the caller says where a period starts (VwapPeriod decides that from the bar
    // time), so these tests state it directly.
    public class VwapCalculatorTests {
        [Fact]
        public void the_first_bar_of_a_period_is_its_own_vwap() {
            VwapCalculator calculator = new();

            VwapSampleModel sample = calculator.Append(typicalPrice: 10.0, volume: 100.0, isDayPeriodStart: true,
                isWeekPeriodStart: true);

            Assert.Equal(10.0, sample.Daily, precision: 6);
            Assert.Equal(10.0, sample.Weekly, precision: 6);
        }

        [Fact]
        public void averages_the_bars_of_the_day_so_far() {
            VwapCalculator calculator = new();

            AppendDayStart(calculator, typicalPrice: 10.0, volume: 100.0);
            VwapSampleModel sample = AppendSameDay(calculator, typicalPrice: 12.0, volume: 100.0);

            // (10×100 + 12×100) / 200 = 11.
            Assert.Equal(11.0, sample.Daily, precision: 6);
        }

        [Fact]
        public void weights_each_bar_by_its_volume() {
            VwapCalculator calculator = new();

            AppendDayStart(calculator, typicalPrice: 10.0, volume: 1.0);
            VwapSampleModel sample = AppendSameDay(calculator, typicalPrice: 20.0, volume: 3.0);

            // (10×1 + 20×3) / 4 = 17.5, not the unweighted 15.
            Assert.Equal(17.5, sample.Daily, precision: 6);
        }

        [Fact]
        public void restarts_the_daily_average_on_a_new_day() {
            VwapCalculator calculator = new();

            AppendDayStart(calculator, typicalPrice: 10.0, volume: 100.0);
            AppendSameDay(calculator, typicalPrice: 12.0, volume: 100.0);
            VwapSampleModel sample = AppendDayStart(calculator, typicalPrice: 20.0, volume: 50.0);

            // Yesterday's bars are dropped, so the day's first bar is its own VWAP again.
            Assert.Equal(20.0, sample.Daily, precision: 6);
        }

        [Fact]
        public void keeps_the_weekly_average_running_across_a_day_boundary() {
            VwapCalculator calculator = new();

            AppendDayStart(calculator, typicalPrice: 10.0, volume: 100.0);
            AppendSameDay(calculator, typicalPrice: 12.0, volume: 100.0);
            VwapSampleModel sample = AppendDayStart(calculator, typicalPrice: 20.0, volume: 50.0);

            // (10×100 + 12×100 + 20×50) / 250 = 12.8 — the new day does not reset the week.
            Assert.Equal(12.8, sample.Weekly, precision: 6);
        }

        [Fact]
        public void restarts_the_weekly_average_on_a_new_week() {
            VwapCalculator calculator = new();

            AppendDayStart(calculator, typicalPrice: 10.0, volume: 100.0);
            AppendSameDay(calculator, typicalPrice: 12.0, volume: 100.0);

            VwapSampleModel sample = calculator.Append(typicalPrice: 20.0, volume: 50.0, isDayPeriodStart: true,
                isWeekPeriodStart: true);

            Assert.Equal(20.0, sample.Weekly, precision: 6);
        }

        [Fact]
        public void has_no_previous_day_value_before_the_first_day_change() {
            VwapCalculator calculator = new();

            VwapSampleModel first = AppendDayStart(calculator, typicalPrice: 10.0, volume: 100.0);
            VwapSampleModel second = AppendSameDay(calculator, typicalPrice: 12.0, volume: 100.0);

            Assert.True(double.IsNaN(first.PreviousDaily));
            Assert.True(double.IsNaN(second.PreviousDaily));
        }

        [Fact]
        public void carries_yesterdays_closing_vwap_through_the_whole_new_day() {
            VwapCalculator calculator = new();

            AppendDayStart(calculator, typicalPrice: 10.0, volume: 100.0);
            AppendSameDay(calculator, typicalPrice: 12.0, volume: 100.0);

            // Yesterday closed at a VWAP of 11; that value is held flat for every bar of the new day.
            VwapSampleModel dayStart = AppendDayStart(calculator, typicalPrice: 20.0, volume: 50.0);
            VwapSampleModel laterThatDay = AppendSameDay(calculator, typicalPrice: 22.0, volume: 50.0);

            Assert.Equal(11.0, dayStart.PreviousDaily, precision: 6);
            Assert.Equal(11.0, laterThatDay.PreviousDaily, precision: 6);
        }

        [Fact]
        public void reports_which_bars_open_a_new_day() {
            VwapCalculator calculator = new();

            Assert.True(AppendDayStart(calculator, typicalPrice: 10.0, volume: 100.0).IsDayPeriodStart);
            Assert.False(AppendSameDay(calculator, typicalPrice: 12.0, volume: 100.0).IsDayPeriodStart);
        }

        [Fact]
        public void falls_back_to_the_bar_price_while_there_is_no_volume() {
            VwapCalculator calculator = new();

            // A dead patch of market would divide by zero; the bar's own hlc3 keeps the line continuous.
            VwapSampleModel sample = AppendDayStart(calculator, typicalPrice: 10.0, volume: 0.0);

            Assert.Equal(10.0, sample.Daily, precision: 6);
            Assert.Equal(10.0, sample.Weekly, precision: 6);
        }

        [Fact]
        public void a_zero_volume_bar_does_not_distort_the_average_that_follows() {
            VwapCalculator calculator = new();

            AppendDayStart(calculator, typicalPrice: 10.0, volume: 0.0);
            VwapSampleModel sample = AppendSameDay(calculator, typicalPrice: 20.0, volume: 100.0);

            // The zero-volume bar contributes nothing to either sum: (20×100) / 100 = 20.
            Assert.Equal(20.0, sample.Daily, precision: 6);
        }

        private static VwapSampleModel AppendDayStart(VwapCalculator calculator, double typicalPrice, double volume) =>
            calculator.Append(typicalPrice, volume, isDayPeriodStart: true, isWeekPeriodStart: false);

        private static VwapSampleModel AppendSameDay(VwapCalculator calculator, double typicalPrice, double volume) =>
            calculator.Append(typicalPrice, volume, isDayPeriodStart: false, isWeekPeriodStart: false);
    }
}
