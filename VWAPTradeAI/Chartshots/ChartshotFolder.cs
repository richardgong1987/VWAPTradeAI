using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

// cAlgo.API has a File type of its own, which clashes with System.IO.File.
using IoFile = System.IO.File;

namespace cAlgo.Robots;

// The folder the entry screenshots go to, numbered in the order they were taken: 1.png, 2.png, …
//
// A picture in the folder is never overwritten: numbering carries on after the highest number
// already there.
//
// Pure: no cAlgo dependency, unit tested.
public class ChartshotFolder {
    private const string FolderName = "TakeChartshot";
    private const string Extension = ".png";

    private int _lastNumber;

    // resetOnStart: delete the numbered pictures of earlier runs, so this run starts at 1 and its
    // pictures line up with the rows of a trade CSV that was reset the same way. Off, the pictures
    // accumulate across runs, as the CSV rows do.
    public ChartshotFolder(bool resetOnStart, string directoryPath) {
        DirectoryPath = directoryPath;
        Directory.CreateDirectory(directoryPath);

        if (resetOnStart)
            DeleteNumberedPictures();

        _lastNumber = HighestNumber();
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

    // Files with other names are not ours, and stay.
    private void DeleteNumberedPictures() {
        foreach (string path in PicturePaths().Where(path => NumberOf(path) > 0).ToList())
            IoFile.Delete(path);
    }

    // 0 when the folder holds no numbered picture yet.
    private int HighestNumber() {
        return PicturePaths().Select(NumberOf).DefaultIfEmpty(0).Max();
    }

    private IEnumerable<string> PicturePaths() {
        return Directory.EnumerateFiles(DirectoryPath, "*" + Extension);
    }

    // 0 for a name that is not a number.
    private static int NumberOf(string path) {
        string name = Path.GetFileNameWithoutExtension(path);
        return int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out int number) ? number : 0;
    }
}
