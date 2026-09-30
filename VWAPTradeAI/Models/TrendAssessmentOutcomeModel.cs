namespace cAlgo.Robots;

// How one request to the trend assessment service ended. Only Assessed can let a trade through.
public enum TrendAssessmentOutcomeModel {
    Assessed, // the model read the chart and judged it
    Unreadable, // the model saw no usable chart in the picture
    Unavailable // no valid answer: no picture, service or model down, timeout, invalid response
}
