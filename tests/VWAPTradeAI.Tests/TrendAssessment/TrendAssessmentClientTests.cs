using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using cAlgo.Robots;
using Xunit;

namespace VWAPTradeAI.Tests.TrendAssessment {
    // The HTTP link to the AI service, against a stand-in handler: what it sends, and that a
    // service which is down, slow or wrong comes back as a result instead of an exception.
    public class TrendAssessmentClientTests {
        private static readonly Uri ServiceUrl = new("http://127.0.0.1:8787");
        private static readonly byte[] ChartPng = { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };

        private const string OkReply = @"{""request_id"": ""req-1"", ""status"": ""OK"", ""trend"": ""DOWN"", ""confidence"": 0.9,
            ""daily_vwap_direction"": ""FALLING"", ""daily_vwap_confidence"": 0.8, ""structure"": ""Lower highs and lower lows"",
            ""reason"": ""Price falls and the daily VWAP slopes down."", ""model"": ""gemma3:27b"", ""elapsed_ms"": 8000}";

        private static TrendAssessmentClient ClientFor(StubHandler handler, double timeoutSeconds = 30) =>
            new(ServiceUrl, TimeSpan.FromSeconds(timeoutSeconds), handler);

        [Fact]
        public async Task an_assessment_is_one_post_with_the_request_id_and_the_picture_as_base64() {
            var handler = new StubHandler(_ => Json(HttpStatusCode.OK, OkReply));
            using TrendAssessmentClient client = ClientFor(handler);

            TrendAssessmentResultModel result = await client.AssessAsync("req-1", ChartPng);

            (HttpMethod method, Uri url, string body) = Assert.Single(handler.Requests);
            Assert.Equal(HttpMethod.Post, method);
            Assert.Equal("http://127.0.0.1:8787/v1/assessments", url.ToString());
            using JsonDocument sent = JsonDocument.Parse(body);
            Assert.Equal("req-1", sent.RootElement.GetProperty("request_id").GetString());
            Assert.Equal(ChartPng, Convert.FromBase64String(sent.RootElement.GetProperty("image_png_base64").GetString()));
            Assert.Equal(TrendAssessmentOutcomeModel.Assessed, result.Outcome);
            Assert.Equal(TrendModel.Down, result.Trend);
        }

        [Fact]
        public async Task a_service_error_comes_back_as_unavailable_with_the_services_words() {
            const string error = @"{""request_id"": ""req-1"", ""error"": {""code"": ""model_unavailable"", ""message"": ""Ollama is not reachable""}}";
            using TrendAssessmentClient client = ClientFor(new StubHandler(_ => Json(HttpStatusCode.BadGateway, error)));

            TrendAssessmentResultModel result = await client.AssessAsync("req-1", ChartPng);

            Assert.Equal(TrendAssessmentOutcomeModel.Unavailable, result.Outcome);
            Assert.Equal("The AI service answered HTTP 502 model_unavailable: Ollama is not reachable", result.Reason);
        }

        [Fact]
        public async Task a_service_that_is_not_running_comes_back_as_unavailable() {
            using TrendAssessmentClient client = ClientFor(new StubHandler(_ => throw new HttpRequestException("Connection refused")));

            TrendAssessmentResultModel result = await client.AssessAsync("req-1", ChartPng);

            Assert.Equal(TrendAssessmentOutcomeModel.Unavailable, result.Outcome);
            Assert.Equal("The AI service is not reachable at http://127.0.0.1:8787/: Connection refused", result.Reason);
        }

        [Fact]
        public async Task no_answer_within_the_limit_comes_back_as_a_timeout() {
            using TrendAssessmentClient client = ClientFor(StubHandler.NeverAnswers(), timeoutSeconds: 0.05);

            TrendAssessmentResultModel result = await client.AssessAsync("req-1", ChartPng);

            Assert.Equal(TrendAssessmentOutcomeModel.Unavailable, result.Outcome);
            Assert.Equal("Local assessment request timed out after 0.05 seconds", result.Reason);
        }

        [Fact]
        public async Task a_request_cut_short_by_the_cbot_stopping_says_so() {
            TrendAssessmentClient client = ClientFor(StubHandler.NeverAnswers());
            Task<TrendAssessmentResultModel> pending = client.AssessAsync("req-1", ChartPng);

            client.Dispose();

            Assert.Equal("The cBot stopped before the AI service answered", (await pending).Reason);
        }

        [Fact]
        public async Task warm_up_posts_to_the_warmup_path_and_reports_nothing_when_the_model_is_loaded() {
            var handler = new StubHandler(_ => Json(HttpStatusCode.OK, @"{""model"": ""gemma3:27b"", ""loaded"": true, ""load_ms"": 9644}"));
            using TrendAssessmentClient client = ClientFor(handler);

            string failure = await client.WarmUpAsync();

            Assert.Null(failure);
            (HttpMethod method, Uri url, _) = Assert.Single(handler.Requests);
            Assert.Equal(HttpMethod.Post, method);
            Assert.Equal("http://127.0.0.1:8787/v1/warmup", url.ToString());
        }

        [Fact]
        public async Task warm_up_reports_why_the_model_could_not_be_loaded() {
            const string error = @"{""request_id"": null, ""error"": {""code"": ""model_unavailable"", ""message"": ""Ollama is not reachable""}}";
            using TrendAssessmentClient client = ClientFor(new StubHandler(_ => Json(HttpStatusCode.BadGateway, error)));

            Assert.Equal("The AI service answered HTTP 502 model_unavailable: Ollama is not reachable", await client.WarmUpAsync());
        }

        [Fact]
        public async Task warm_up_reports_a_service_that_is_not_running() {
            using TrendAssessmentClient client = ClientFor(new StubHandler(_ => throw new HttpRequestException("Connection refused")));

            Assert.Equal("The AI service is not reachable at http://127.0.0.1:8787/: Connection refused", await client.WarmUpAsync());
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        // Stands in for the network: records each request and answers with the given function.
        private sealed class StubHandler : HttpMessageHandler {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

            public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) {
                _respond = respond;
            }

            public List<(HttpMethod Method, Uri Url, string Body)> Requests { get; } = new();

            // Waits until the client gives up, as a service that never answers would make it.
            public static StubHandler NeverAnswers() => new(null);

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
                string body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
                Requests.Add((request.Method, request.RequestUri, body));

                if (_respond == null)
                    await Task.Delay(Timeout.Infinite, cancellationToken);

                return _respond(request);
            }
        }
    }
}
