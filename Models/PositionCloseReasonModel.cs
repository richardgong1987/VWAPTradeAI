namespace cAlgo.Robots;

// Why a position closed, kept free of cAlgo.API.PositionCloseReason (CAlgoBroker maps it).
public enum PositionCloseReasonModel {
    Closed, // closed by hand or by the platform, not by a price level
    StopLoss,
    TakeProfit,
    StopOut // margin call
}
