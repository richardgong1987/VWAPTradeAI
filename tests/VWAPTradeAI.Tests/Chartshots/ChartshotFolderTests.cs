using System;
using System.IO;
using System.Linq;
using cAlgo.Robots;
using Xunit;

namespace VWAPTradeAI.Tests.Chartshots {
    // How the entry screenshots are numbered. What matters most is that a picture already in the
    // folder is never overwritten, by a restart or by a second instance.
    public class ChartshotFolderTests : IDisposable {
        private static readonly byte[] Png = { 1, 2, 3 };

        private readonly string _directory = Path.Combine(Path.GetTempPath(), "ChartshotFolderTests-" + Guid.NewGuid());

        public void Dispose() {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }

        private string[] FileNames() => Directory.GetFiles(_directory).Select(Path.GetFileName).OrderBy(name => name).ToArray();

        [Fact]
        public void the_folder_is_takechartshot_under_documents() {
            Assert.Equal(Path.Combine("/docs", "TakeChartshot"), ChartshotFolder.DirectoryIn("/docs"));
        }

        [Fact]
        public void pictures_are_numbered_from_1_in_the_order_they_are_saved() {
            var folder = new ChartshotFolder(_directory);

            string first = folder.Save(Png);
            folder.Save(Png);
            folder.Save(Png);

            Assert.Equal(Path.Combine(_directory, "1.png"), first);
            Assert.Equal(new[] { "1.png", "2.png", "3.png" }, FileNames());
            Assert.Equal(Png, File.ReadAllBytes(first));
        }

        [Fact]
        public void a_restart_carries_on_after_the_highest_number_in_the_folder() {
            var firstRun = new ChartshotFolder(_directory);
            firstRun.Save(Png);
            firstRun.Save(Png);
            // A deleted picture leaves a gap; filling it would put a newer picture before an older one.
            File.Delete(Path.Combine(_directory, "1.png"));

            string saved = new ChartshotFolder(_directory).Save(Png);

            Assert.Equal("3.png", Path.GetFileName(saved));
        }

        [Fact]
        public void files_that_are_not_numbered_pictures_do_not_affect_the_numbering() {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(Path.Combine(_directory, "notes.png"), "");
            File.WriteAllText(Path.Combine(_directory, "9.txt"), "");

            string saved = new ChartshotFolder(_directory).Save(Png);

            Assert.Equal("1.png", Path.GetFileName(saved));
        }

        [Fact]
        public void a_number_another_instance_took_meanwhile_is_skipped_not_overwritten() {
            var folder = new ChartshotFolder(_directory);
            File.WriteAllBytes(Path.Combine(_directory, "1.png"), new byte[] { 9 });

            string saved = folder.Save(Png);

            Assert.Equal("2.png", Path.GetFileName(saved));
            Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(Path.Combine(_directory, "1.png")));
        }
    }
}
