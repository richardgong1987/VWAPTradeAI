using System;

namespace cAlgo.Robots;

// A position the broker has just opened, as the broker reports it (see IBroker). Pure data.
public class PositionEntryModel {
    public int PositionId { get; set; }

    public DateTime EntryTime { get; set; }

    public double EntryPrice { get; set; }

    // The stop the broker actually set; null when it set none, and the plan's stop stands in.
    public double? StopLoss { get; set; }

    public double VolumeInUnits { get; set; }

    // The opening deal's ID; empty when the broker reports no deal.
    public string DealId { get; set; } = "";
}
