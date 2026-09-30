using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using cAlgo.Robots;
using Xunit;

namespace VWAPTradeAI.Tests.TrendAssessment {
    // Runs only when RUN_AI_SERVICE_TESTS=1: needs the TrendAssessmentModel service on 127.0.0.1:8787.
    public sealed class AiServiceFactAttribute : FactAttribute {
        public const string PictureVariable = "AI_SERVICE_TEST_PNG";

        public AiServiceFactAttribute(bool needsPicture = false) {
            if (Environment.GetEnvironmentVariable("RUN_AI_SERVICE_TESTS") != "1")
                Skip = "Needs the local TrendAssessmentModel service; set RUN_AI_SERVICE_TESTS=1.";
            else if (needsPicture && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(PictureVariable)))
                Skip = $"Set {PictureVariable} to the path of a chart screenshot.";
        }
    }

    // The cBot's client against the real service and model: proof that the two projects agree on
    // the HTTP contract. Everything else about the client is tested without a network.
    public class TrendAssessmentServiceTests {
        private static readonly Uri ServiceUrl = new("http://127.0.0.1:8787");

        [AiServiceFact]
        public async Task the_real_service_warms_up_and_answers_a_blank_picture_with_unreadable() {
            using var client = new TrendAssessmentClient(ServiceUrl, TimeSpan.FromSeconds(30));

            Assert.Null(await client.WarmUpAsync());

            TrendAssessmentResultModel result = await client.AssessAsync(Guid.NewGuid().ToString("N"), BlankPng(width: 800, height: 300));

            Assert.Equal(TrendAssessmentOutcomeModel.Unreadable, result.Outcome);
            Assert.Equal("gemma3:27b", result.ModelName);
            Assert.StartsWith("Chart unreadable: ", TrendDirectionGate.FindRejectReason(SignalSideModel.Buy, result));
        }

        [AiServiceFact(needsPicture: true)]
        public async Task the_real_service_judges_a_real_chart_picture() {
            byte[] chartPng = File.ReadAllBytes(Environment.GetEnvironmentVariable(AiServiceFactAttribute.PictureVariable));
            using var client = new TrendAssessmentClient(ServiceUrl, TimeSpan.FromSeconds(30));

            TrendAssessmentResultModel result = await client.AssessAsync(Guid.NewGuid().ToString("N"), chartPng);

            Assert.Equal(TrendAssessmentOutcomeModel.Assessed, result.Outcome);
            Assert.NotNull(result.Trend);
            Assert.NotNull(result.DailyVwapDirection);
            Assert.InRange(result.TrendConfidence.Value, 0.0, 1.0);
            Assert.False(string.IsNullOrWhiteSpace(result.Reason));
        }

        // A plain white PNG, built by hand so the test needs no picture on disk.
        private static byte[] BlankPng(int width, int height) {
            var whiteRow = new byte[1 + width * 3]; // filter byte 0, then RGB
            Array.Fill(whiteRow, (byte)0xFF, 1, width * 3);

            using var pixels = new MemoryStream();

            using (var zlib = new ZLibStream(pixels, CompressionLevel.Fastest, leaveOpen: true)) {
                for (int y = 0; y < height; y++)
                    zlib.Write(whiteRow);
            }

            using var png = new MemoryStream();
            png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
            WriteChunk(png, "IHDR", Concat(BigEndian((uint)width), BigEndian((uint)height), new byte[] { 8, 2, 0, 0, 0 }));
            WriteChunk(png, "IDAT", pixels.ToArray());
            WriteChunk(png, "IEND", Array.Empty<byte>());
            return png.ToArray();
        }

        private static void WriteChunk(Stream png, string kind, byte[] data) {
            byte[] kindAndData = Concat(Encoding.ASCII.GetBytes(kind), data);
            png.Write(BigEndian((uint)data.Length));
            png.Write(kindAndData);
            png.Write(BigEndian(Crc32(kindAndData)));
        }

        // PNG's checksum; the BCL has none without an extra package.
        private static uint Crc32(byte[] bytes) {
            uint crc = 0xFFFFFFFF;

            foreach (byte value in bytes) {
                crc ^= value;

                for (int bit = 0; bit < 8; bit++)
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }

            return ~crc;
        }

        private static byte[] BigEndian(uint value) =>
            new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value };

        private static byte[] Concat(params byte[][] parts) {
            using var all = new MemoryStream();

            foreach (byte[] part in parts)
                all.Write(part);

            return all.ToArray();
        }
    }
}
