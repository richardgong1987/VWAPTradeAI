namespace cAlgo.Robots;

// Trade side, kept free of cAlgo.API.TradeType so the planner stays pure and testable.
// CAlgoBroker maps it to TradeType only at the broker boundary.
public enum TradeDirectionModel {
    Long,
    Short
}
