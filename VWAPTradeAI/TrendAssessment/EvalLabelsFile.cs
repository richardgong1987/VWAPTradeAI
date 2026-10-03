using System.IO;
using System.Linq;
using System.Text;

// cAlgo.API has a File type of its own, which clashes with System.IO.File.
using IoFile = System.IO.File;

namespace cAlgo.Robots;

// labels.csv in the evaluation folder: one row per picture, saying what it really shows. The
// columns are the ones evaluate.py reads, as TrendAssessmentModel's docs/CORRECTING_THE_MODEL.md
// defines them:
//
//   file,trend,daily_vwap_direction,reason
//   20260930-220500_XAUUSD_7f3c2a9e.png,,,
//
// A picture is listed with its labels blank. They are a person's judgement, made before looking
// at the model's answer, so nothing here fills them in. Rows already in the file, labelled or
// not, are never changed, and a picture is never listed twice.
//
// Pure: no cAlgo dependency, unit tested.
public class EvalLabelsFile {
    private const string FileName = "labels.csv";
    private const string Header = "file,trend,daily_vwap_direction,reason";

    // No byte-order mark and plain \n line breaks: the file is read by Python's csv module and
    // edited by hand, and both of those read it the same on any machine.
    private static readonly Encoding Utf8 = new UTF8Encoding(false);
    private const string LineBreak = "\n";

    public EvalLabelsFile(string evalDirectoryPath) {
        FilePath = Path.Combine(evalDirectoryPath, FileName);
    }

    public string FilePath { get; }

    // Adds an unlabelled row for the picture unless the file lists it already. Without a file, one
    // is started with the header.
    public void EnsureListed(string pictureFileName) {
        string row = pictureFileName + ",,,";
        string text = IoFile.Exists(FilePath) ? IoFile.ReadAllText(FilePath, Utf8) : "";

        if (text.Length == 0) {
            IoFile.WriteAllText(FilePath, Header + LineBreak + row + LineBreak, Utf8);
            return;
        }

        if (IsListed(text, pictureFileName))
            return;

        // A file last saved by hand may not end with a line break.
        string endOfLastRow = text.EndsWith(LineBreak) ? "" : LineBreak;
        IoFile.AppendAllText(FilePath, endOfLastRow + row + LineBreak, Utf8);
    }

    // The file column is the first cell of every row below the header. Trim also drops the \r of a
    // file saved with Windows line breaks.
    private static bool IsListed(string text, string pictureFileName) {
        return text.Split(LineBreak).Skip(1).Any(line => line.Split(',')[0].Trim().Trim('"') == pictureFileName);
    }
}
