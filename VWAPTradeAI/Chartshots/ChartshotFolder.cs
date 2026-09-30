using System.Globalization;
using System.IO;
using System.Linq;

// cAlgo.API has a File type of its own, which clashes with System.IO.File.
using IoFile = System.IO.File;

namespace cAlgo.Robots;

// The folder the entry screenshots go to, numbered in the order they were taken: 1.png, 2.png, …
//
// Numbering carries on after the highest number already in the folder, so a restart or a new
// backtest adds to the pictures that are there and never overwrites one.
//
// Pure: no cAlgo dependency, unit tested.
public class ChartshotFolder {
    private const string FolderName = "TakeChartshot";
    private const string Extension = ".png";

    private int _lastNumber;

    public ChartshotFolder(string directoryPath) {
        DirectoryPath = directoryPath;
        Directory.CreateDirectory(directoryPath);
        _lastNumber = HighestNumberIn(directoryPath);
    }

    public static string DirectoryIn(string documentsPath) {
        return Path.Combine(documentsPath, FolderName);
    }

    public string DirectoryPath { get; }

    // Writes the picture under the next number and returns its path.
    public string Save(byte[] png) {
        string path;

        // Another instance (the same cBot on a second symbol) shares the folder and may have taken
        // this number since the folder was read.
        do {
            _lastNumber++;
            path = Path.Combine(DirectoryPath, _lastNumber.ToString(CultureInfo.InvariantCulture) + Extension);
        } while (IoFile.Exists(path));

        IoFile.WriteAllBytes(path, png);
        return path;
    }

    // 0 when the folder holds no numbered picture yet. Files with other names are not ours.
    private static int HighestNumberIn(string directoryPath) {
        return Directory.EnumerateFiles(directoryPath, "*" + Extension)
            .Select(NumberOf)
            .DefaultIfEmpty(0)
            .Max();
    }

    private static int NumberOf(string path) {
        string name = Path.GetFileNameWithoutExtension(path);
        return int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out int number) ? number : 0;
    }
}
