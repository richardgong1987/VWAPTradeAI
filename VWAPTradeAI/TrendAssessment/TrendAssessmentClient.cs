using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace cAlgo.Robots;

// The link to the local TrendAssessmentModel service, which owns everything about the AI model:
// which model, the prompt, keeping it loaded. This class knows only the service's HTTP contract:
//
//   POST /v1/assessments   {"request_id", "image_png_base64"}  →  an assessment, or an error
//   POST /v1/warmup        (no body)                           →  the model is loaded
//
// Nothing here throws for a failed request: a service that is down, slow or wrong comes back as an
// Unavailable result, which the gate rejects. It uses .NET's HttpClient, so it never runs on, or
// blocks, the cBot thread while the model thinks.
//
// Pattern: Adapter. No cAlgo dependency, so it is unit tested against a stand-in HTTP handler.
public sealed class TrendAssessmentClient : IDisposable {
    // Loading the model from disk takes far longer than one assessment; the service allows 120 s.
    private static readonly TimeSpan WarmUpTimeout = TimeSpan.FromSeconds(150);

    private readonly HttpClient _http;
    private readonly Uri _serviceUrl;
    private readonly TimeSpan _assessmentTimeout;

    // Cancels whatever is still in flight when the cBot stops.
    private readonly CancellationTokenSource _stopping = new();

    // handler: tests replace the network here.
    public TrendAssessmentClient(Uri serviceUrl, TimeSpan assessmentTimeout, HttpMessageHandler handler = null) {
        _serviceUrl = serviceUrl;
        _assessmentTimeout = assessmentTimeout;
        // The service is on this machine: the chart must never travel through a system proxy.
        // Each request carries its own time limit, so the client's single one is switched off.
        _http = new HttpClient(handler ?? new HttpClientHandler { UseProxy = false }) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task<TrendAssessmentResultModel> AssessAsync(string requestId, byte[] chartPng) {
        string body = JsonSerializer.Serialize(new Dictionary<string, string> {
            ["request_id"] = requestId,
            ["image_png_base64"] = Convert.ToBase64String(chartPng)
        });

        try {
            using HttpResponseMessage response = await PostAsync("v1/assessments", body, _assessmentTimeout).ConfigureAwait(false);
            string reply = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return TrendAssessmentReply.Read((int)response.StatusCode, reply);
        } catch (OperationCanceledException) {
            return TrendAssessmentResultModel.Unavailable(DescribeCancellation(_assessmentTimeout));
        } catch (HttpRequestException error) {
            return TrendAssessmentResultModel.Unavailable($"The AI service is not reachable at {_serviceUrl}: {error.Message}");
        }
    }

    // Null when the model is loaded; otherwise why it could not be.
    public async Task<string> WarmUpAsync() {
        try {
            using HttpResponseMessage response = await PostAsync("v1/warmup", "", WarmUpTimeout).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
                return null;

            string reply = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return TrendAssessmentReply.Read((int)response.StatusCode, reply).Reason;
        } catch (OperationCanceledException) {
            return DescribeCancellation(WarmUpTimeout);
        } catch (HttpRequestException error) {
            return $"The AI service is not reachable at {_serviceUrl}: {error.Message}";
        }
    }

    public void Dispose() {
        _stopping.Cancel();
        _http.Dispose();
        _stopping.Dispose();
    }

    private async Task<HttpResponseMessage> PostAsync(string path, string jsonBody, TimeSpan timeout) {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
        limit.CancelAfter(timeout);

        using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        return await _http.PostAsync(new Uri(_serviceUrl, path), content, limit.Token).ConfigureAwait(false);
    }

    private string DescribeCancellation(TimeSpan timeout) {
        return _stopping.IsCancellationRequested
            ? "The cBot stopped before the AI service answered"
            : $"Local assessment request timed out after {timeout.TotalSeconds:0.##} seconds";
    }
}
