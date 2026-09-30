using System;
using System.Collections.Generic;
using cAlgo.Robots;
using Xunit;

namespace DayTradeSelf.Tests.Vwap {
    // Walks a week of 5-minute bars through the same steps VwapSeries takes, so the period rules
    // and the accumulator are exercised together. The unit tests check each rule in isolation; this
    // catches the case where both are individually right and still produce a wrong chart — which is
    // how the weekly VWAP once ended up resetting every morning and matching the daily one exactly.
    public class VwapSeriesWalkTests {
        [Fact]
        public void the_weekly_vwap_stops_matching_the_daily_one_after_the_first_day() {
            List<VwapSampleModel> week = WalkOneWeek();

            // Monday is the one day where both legitimately coincide: the week and the day both
            // start at Monday 06:00, so they accumulate the same bars.
            Assert.All(BarsOfDayStartingOn(week, dayOfMonth: 21), sample => Assert.Equal(sample.Daily, sample.Weekly, precision: 9));

            // From Tuesday on the day restarts while the week keeps running, so they must differ.
            foreach (int dayOfMonth in new[] { 22, 23, 24, 25 }) {
                List<VwapSampleModel> day = BarsOfDayStartingOn(week, dayOfMonth);
                VwapSampleModel last = day[day.Count - 1];

                Assert.True(Math.Abs(last.Daily - last.Weekly) > 1.0,
                    $"day {dayOfMonth}: daily {last.Daily} and weekly {last.Weekly} should have diverged");
            }
        }

        [Fact]
        public void the_day_restarts_every_morning_and_the_week_only_on_monday() {
            List<VwapSampleModel> week = WalkOneWeek();

            // Day starts: the first bar of the series, then 06:00 on Mon/Tue/Wed/Thu/Fri/Sat.
            Assert.Equal(7, week.FindAll(sample => sample.IsDayPeriodStart).Count);

            // Week starts: the first bar of the series, then Monday 06:00.
            Assert.Equal(2, week.FindAll(sample => sample.IsWeekPeriodStart).Count);
        }

        [Fact]
        public void every_bar_carries_a_value() {
            List<VwapSampleModel> week = WalkOneWeek();

            // Every bar falls in some period, Monday and the early morning included, so nothing is
            // ever blank.
            Assert.All(week, sample => {
                Assert.False(double.IsNaN(sample.Daily));
                Assert.False(double.IsNaN(sample.Weekly));
            });
        }

        // Monday 00:00 through Saturday 12:00 in 5-minute bars. The price trends steadily upward so
        // a day's average and the week's running average genuinely separate — an oscillation around
        // a fixed level would make both converge to that level and prove nothing.
        private static List<VwapSampleModel> WalkOneWeek() {
            List<VwapSampleModel> samples = new();
            VwapCalculator calculator = new();
            DateTime cursor = new(2026, 9, 21, 0, 0, 0); // Monday
            DateTime end = new(2026, 9, 26, 12, 0, 0); // Saturday noon
            DateTime? previous = null;
            double price = 4000.0;

            for (int i = 0; cursor < end; i++) {
                price += 0.5 + (i % 5 - 2) * 0.1; // steady uptrend with a small wiggle

                VwapSampleModel sample = calculator.Append(price, volume: 100.0, VwapPeriod.IsNewDay(cursor, previous),
                    VwapPeriod.IsNewWeek(cursor, previous));

                sample.OpenTime = cursor;
                samples.Add(sample);
                previous = cursor;
                cursor = cursor.AddMinutes(5);
            }

            return samples;
        }

        private static List<VwapSampleModel> BarsOfDayStartingOn(List<VwapSampleModel> week, int dayOfMonth) {
            DateTime dayStart = new(2026, 9, dayOfMonth, 6, 0, 0);

            return week.FindAll(sample => VwapPeriod.GetDayStart(sample.OpenTime) == dayStart);
        }
    }
}
