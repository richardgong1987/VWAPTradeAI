using System;
using System.Collections.Generic;
using cAlgo.Robots;

namespace DayTradeSelf.Tests {
    // Stands in for CAlgoBroker: an account, a quote and positions the test sets directly. It records
    // every order it is asked to place, fills it at the plan's entry unless told to refuse, and
    // closes positions on demand.
    internal sealed class FakeBroker : IBroker {
        public string SymbolName { get; init; } = "XAUUSD";

        public double AccountEquity { get; set; } = 10000.0;

        public double Bid { get; set; }

        public double Ask { get; set; }

        // Labels of the positions currently open.
        public HashSet<string> OpenLabels { get; } = new();

        // When set, every order is refused with this error.
        public string Refusal { get; set; }

        public List<(OrderPlanModel Plan, string Label, string Comment)> Orders { get; } = new();

        public event Action<PositionCloseModel> PositionClosed;

        public bool HasOpenPosition(string label) => OpenLabels.Contains(label);

        public BrokerOrderResultModel PlaceMarketOrder(OrderPlanModel plan, string label, string comment) {
            Orders.Add((plan, label, comment));

            if (Refusal != null)
                return BrokerOrderResultModel.Refused(Refusal);

            return BrokerOrderResultModel.Filled(new PositionEntryModel {
                PositionId = 7,
                EntryPrice = plan.EntryPrice,
                StopLoss = plan.StopPrice,
                VolumeInUnits = plan.VolumeInUnits,
                DealId = "70"
            });
        }

        public void Close(PositionCloseModel position) => PositionClosed?.Invoke(position);
    }
}
