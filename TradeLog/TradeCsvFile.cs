using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

// cAlgo.API 也有个 File 类型，会跟 System.IO.File 撞名。
using IoFile = System.IO.File;

namespace cAlgo.Robots;

// 交易 CSV 这个文件本身：它在哪、表头对不对、怎么往里追加一行。
// 一行里放哪些事实由 TradeCsvLogger 决定，有哪些列由 TradeCsvColumns 决定。
public class TradeCsvFile {
    private static readonly Encoding CsvEncoding = new UTF8Encoding(true);

    // resetOnStart: 用一份新表头覆盖整个文件，只保留本次运行。文件是固定名、只追加的，
    // 不清空的话每跑一次回测就叠一份同样的交易。Off, rows accumulate across runs — unless the
    // file was written with other columns, in which case it is moved aside first (ArchiveIfOutdated).
    //
    // reportsDirectory: the running mode's folder (see ReportsDirectory). An absolute fileName — the
    // backtest scripts (run_conditions) pass one — is used as is and reportsDirectory ignored.
    public TradeCsvFile(bool resetOnStart, string reportsDirectory, string fileName) {
        FilePath = Path.IsPathRooted(fileName) ? fileName : Path.Combine(reportsDirectory, fileName);
        EnsureDirectoryExists(FilePath);

        if (resetOnStart || !IoFile.Exists(FilePath))
            WriteHeader();
        else
            ArchiveIfOutdated();
    }

    // One folder per running mode, so runs never overwrite each other: the backtest folder is rebuilt
    // by the scripts on every run, the demo and live folders only ever grow.
    public static string ReportsDirectory(string documentsPath, bool isBacktesting, bool isLiveAccount) {
        string folder = isBacktesting ? "trading_reports" : isLiveAccount ? "release_trading_reports" : "simulate_trading_reports";
        return Path.Combine(documentsPath, folder);
    }

    public string FilePath { get; }

    // Where a file with other columns was moved at start-up; null when nothing was moved.
    public string ArchivedFilePath { get; private set; }

    public void AppendLine(string line) {
        IoFile.AppendAllText(FilePath, line + Environment.NewLine, CsvEncoding);
    }

    private void WriteHeader() {
        IoFile.WriteAllText(FilePath, TradeCsvColumns.Header + Environment.NewLine, CsvEncoding);
    }

    // Rows must only ever sit under the header they were written with. A file from a build with
    // other columns is renamed aside — never rewritten or deleted — and a fresh file starts.
    private void ArchiveIfOutdated() {
        string header = IoFile.ReadLines(FilePath, CsvEncoding).FirstOrDefault();

        if (header == TradeCsvColumns.Header)
            return;

        // An empty file has nothing worth keeping.
        if (header != null) {
            ArchivedFilePath = ArchivePathFor(FilePath, DateTime.Now);
            IoFile.Move(FilePath, ArchivedFilePath);
        }

        WriteHeader();
    }

    // DayTradeSelfs.csv → DayTradeSelfs.old-20260927-215900.csv, in the same folder.
    private static string ArchivePathFor(string filePath, DateTime time) {
        string name = Path.GetFileNameWithoutExtension(filePath) + ".old-" +
                      time.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + Path.GetExtension(filePath);
        return Path.Combine(Path.GetDirectoryName(filePath) ?? "", name);
    }

    private static void EnsureDirectoryExists(string filePath) {
        string directory = Path.GetDirectoryName(filePath);

        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
    }
}
