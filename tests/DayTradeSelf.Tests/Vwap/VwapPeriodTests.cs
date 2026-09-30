using System;
using cAlgo.Robots;
using Xunit;

namespace DayTradeSelf.Tests.Vwap {
    // The indicator's own periods. In Japan time:
    //   a VWAP day  runs 06:00 -> 06:00 the next morning, every day of the week;
    //   a VWAP week runs Monday 06:00 -> the following Monday 06:00.
    // 2026-09-21 is a Monday.
    public class VwapPeriodTests {
        private static readonly DateTime Sunday = new(2026, 9, 20);
        private static readonly DateTime Monday = new(2026, 9, 21);
        private static readonly DateTime Tuesday = new(2026, 9, 22);
        private static readonly DateTime Wednesday = new(2026, 9, 23);
        private static readonly DateTime Friday = new(2026, 9, 25);
        private static readonly DateTime Saturday = new(2026, 9, 26);

        [Fact]
        public void the_day_starts_at_six_in_the_morning() {
            Assert.Equal(At(Tuesday, 6, 0), VwapPeriod.GetDayStart(At(Tuesday, 6, 0)));
            Assert.Equal(At(Tuesday, 6, 0), VwapPeriod.GetDayStart(At(Tuesday, 10, 30)));
            Assert.Equal(At(Tuesday, 6, 0), VwapPeriod.GetDayStart(At(Wednesday, 5, 59)));
        }

        [Fact]
        public void before_six_belongs_to_the_previous_day() {
            Assert.Equal(At(Monday, 6, 0), VwapPeriod.GetDayStart(At(Tuesday, 5, 59)));
        }

        [Fact]
        public void the_daily_vwap_resets_at_six_and_nowhere_else() {
            Assert.True(VwapPeriod.IsNewDay(At(Wednesday, 6, 0), At(Wednesday, 5, 55)));
            Assert.False(VwapPeriod.IsNewDay(At(Wednesday, 0, 0), At(Tuesday, 23, 55))); // not at midnight
            Assert.False(VwapPeriod.IsNewDay(At(Wednesday, 10, 30), At(Wednesday, 10, 25))); // not mid-morning
        }

        [Fact]
        public void monday_is_a_vwap_day_like_any_other() {
            Assert.Equal(At(Monday, 6, 0), VwapPeriod.GetDayStart(At(Monday, 12, 0)));
            Assert.True(VwapPeriod.IsNewDay(At(Monday, 6, 0), At(Monday, 5, 55)));
        }

        [Fact]
        public void the_week_starts_on_monday_morning() {
            DateTime expected = At(Monday, 6, 0);

            Assert.Equal(expected, VwapPeriod.GetWeekStart(At(Monday, 6, 0)));
            Assert.Equal(expected, VwapPeriod.GetWeekStart(At(Tuesday, 3, 0)));
            Assert.Equal(expected, VwapPeriod.GetWeekStart(At(Wednesday, 14, 0)));
            Assert.Equal(expected, VwapPeriod.GetWeekStart(At(Friday, 23, 0)));
            Assert.Equal(expected, VwapPeriod.GetWeekStart(At(Saturday, 5, 59)));
        }

        [Fact]
        public void monday_before_six_still_belongs_to_the_previous_week() {
            Assert.Equal(At(Monday.AddDays(-7), 6, 0), VwapPeriod.GetWeekStart(At(Monday, 5, 59)));
            Assert.Equal(At(Monday.AddDays(-7), 6, 0), VwapPeriod.GetWeekStart(At(Sunday, 12, 0)));
        }

        [Fact]
        public void the_weekly_vwap_resets_once_a_week() {
            Assert.True(VwapPeriod.IsNewWeek(At(Monday, 6, 0), At(Monday, 5, 55)));
            Assert.False(VwapPeriod.IsNewWeek(At(Wednesday, 6, 0), At(Wednesday, 5, 55))); // a new day, same week
            Assert.False(VwapPeriod.IsNewWeek(At(Tuesday, 10, 30), At(Tuesday, 10, 25)));
        }

        [Fact]
        public void the_first_bar_of_a_series_opens_both_periods() {
            Assert.True(VwapPeriod.IsNewDay(At(Wednesday, 14, 0), previous: null));
            Assert.True(VwapPeriod.IsNewWeek(At(Wednesday, 14, 0), previous: null));
        }

        private static DateTime At(DateTime day, int hour, int minute) => day.Date.AddHours(hour).AddMinutes(minute);
    }
}
