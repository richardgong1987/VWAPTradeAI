using System;
using System.IO;
using System.Linq;
using cAlgo.Robots;
using Xunit;

namespace VWAPTradeAI.Tests.Chartshots {
    // How the entry screenshots are numbered. What matters most is that a picture is never
    // overwritten, by a restart or by a second instance, and that a reset deletes only our own.
    public class ChartshotFolderTests : IDisposable {
        private static readonly byte[] Png = { 1, 2, 3 };

        private readonly string _directory = Path.Combine(Path.GetTempPath(), "ChartshotFolderTests-" + Guid.NewGuid());

        public void Dispose() {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }

        private ChartshotFolder Open(bool resetOnStart = false) => new(resetOnStart, _directory);

        private string[] FileNames() => Directory.GetFiles(_directory).Select(Path.GetFileName).OrderBy(name => name).ToArray();

        [Fact]
        public void the_folder_is_takechartshot_under_documents() {
            Assert.Equal(Path.Combine("/docs", "TakeChartshot"), ChartshotFolder.DirectoryIn("/docs"));
        }

        [Fact]
        public void pictures_are_numbered_from_1_in_the_order_they_are_saved() {
            ChartshotFolder folder = Open();

            string first = folder.Save(Png);
            folder.Save(Png);
            folder.Save(Png);

            Assert.Equal(Path.Combine(_directory, "1.png"), first);
            Assert.Equal(new[] { "1.png", "2.png", "3.png" }, FileNames());
            Assert.Equal(Png, File.ReadAllBytes(first));
        }

        [Fact]
        public void without_a_reset_a_restart_carries_on_after_the_highest_number_in_the_folder() {
            ChartshotFolder firstRun = Open();
            firstRun.Save(Png);
            firstRun.Save(Png);
            // A deleted picture leaves a gap; filling it would put a newer picture before an older one.
            File.Delete(Path.Combine(_directory, "1.png"));

            string saved = Open().Save(Png);

            Assert.Equal("3.png", Path.GetFileName(saved));
        }

        [Fact]
        public void reset_on_start_deletes_the_earlier_runs_pictures_and_starts_again_from_1() {
            ChartshotFolder firstRun = Open();
            firstRun.Save(Png);
            firstRun.Save(Png);

            ChartshotFolder secondRun = Open(resetOnStart: true);

            Assert.Empty(FileNames());
            Assert.Equal("1.png", Path.GetFileName(secondRun.Save(Png)));
        }

        [Fact]
        public void reset_on_start_leaves_files_that_are_not_numbered_pictures() {
            Open().Save(Png);
            File.WriteAllText(Path.Combine(_directory, "notes.png"), "");
            File.WriteAllText(Path.Combine(_directory, "9.txt"), "");

            Open(resetOnStart: true);

            Assert.Equal(new[] { "9.txt", "notes.png" }, FileNames());
        }

        [Fact]
        public void files_that_are_not_numbered_pictures_do_not_affect_the_numbering() {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(Path.Combine(_directory, "notes.png"), "");
            File.WriteAllText(Path.Combine(_directory, "9.txt"), "");

            string saved = Open().Save(Png);

            Assert.Equal("1.png", Path.GetFileName(saved));
        }

        [Fact]
        public void a_number_another_instance_took_meanwhile_is_skipped_not_overwritten() {
            ChartshotFolder folder = Open();
            File.WriteAllBytes(Path.Combine(_directory, "1.png"), new byte[] { 9 });

            string saved = folder.Save(Png);

            Assert.Equal("2.png", Path.GetFileName(saved));
            Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(Path.Combine(_directory, "1.png")));
        }
    }
}
