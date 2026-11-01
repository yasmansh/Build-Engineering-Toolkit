using System.Diagnostics;
using System.Globalization;
using System.Xml.Linq;
using ClosedXML.Excel;

namespace BuildCopyPathComparer;

/*
 * Compares "where files landed in the build output" vs "where the product model says they should go".
 *
 * Usage:
 *   BuildCopyPathComparer <buildAppRoot> [--model path\to\ProductModel.xml] [--out ReportFolder]
 *
 * Example:
 *   BuildCopyPathComparer D:\Lab\Builds\App-net8.0 --out D:\Lab\Reports
 */
internal static class Program
{
    private static readonly string[] SkipPathParts =
    [
        @"\ConvertScripts\",
        @"\LanguagePacks\",
        @"\Reports\",
        @"\ComponentModel\",
        @"\Diagnostics\"
    ];

    private static readonly string[] SkipNameSuffixes =
    [
        ".deps.json",
        ".dll.config",
        "resources.dll",
        ".pdb",
        ".log"
    ];

    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintHelp();
            return args.Length == 0 ? 1 : 0;
        }

        var buildRoot = EnsureTrailingSlash(args[0]);
        var modelPath = GetOption(args, "--model")
                        ?? Path.Combine(buildRoot, "ComponentModel", "ProductModel.xml");
        var outDir = GetOption(args, "--out") ?? Directory.GetCurrentDirectory();

        if (!Directory.Exists(buildRoot))
        {
            Console.Error.WriteLine($"Build folder not found: {buildRoot}");
            return 1;
        }

        if (!File.Exists(modelPath))
        {
            Console.Error.WriteLine($"Product model file not found: {modelPath}");
            return 1;
        }

        Directory.CreateDirectory(outDir);

        var stamp = DateTime.Now.ToString("yyyy-MMdd-HH-mm", CultureInfo.InvariantCulture);
        var buildFiles = ScanBuildOutput(buildRoot, Path.Combine(outDir, $"Report-BuildOutput-{stamp}.xlsx"));
        var modelFiles = ScanProductModel(modelPath, Path.Combine(outDir, $"Report-ProductModel-{stamp}.xlsx"));
        var diffs = Compare(buildFiles, modelFiles);

        SaveDiffExcel(diffs, Path.Combine(outDir, $"Report-Diff-{stamp}.xlsx"));

        Console.WriteLine($"Build files: {buildFiles.Count}");
        Console.WriteLine($"Model files: {modelFiles.Count}");
        Console.WriteLine($"CopyPath differences: {diffs.Count}");
        Console.WriteLine($"Reports saved under: {outDir}");
        return 0;
    }

    private static List<FileEntry> ScanBuildOutput(string buildOutputDir, string excelPath)
    {
        var files = Directory.GetFiles(buildOutputDir, "*.*", SearchOption.AllDirectories)
            .Where(path => !SkipPathParts.Any(part => path.Contains(part, StringComparison.OrdinalIgnoreCase)))
            .Where(path => !SkipNameSuffixes.Any(suffix => path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
            .Select(path =>
            {
                var name = Path.GetFileName(path);
                var relativeDir = Path.GetDirectoryName(Path.GetRelativePath(buildOutputDir, path)) ?? string.Empty;
                return new
                {
                    Name = name,
                    Version = SafeFileVersion(path),
                    CopyPath = relativeDir.Replace('/', '\\')
                };
            })
            .ToList();

        var grouped = files
            .GroupBy(x => (x.Name, x.Version))
            .Select(g => new FileEntry(
                g.Key.Name,
                g.Key.Version ?? string.Empty,
                QuoteJoin(g.Select(i => i.CopyPath).Where(p => !p.Contains("Presentation", StringComparison.OrdinalIgnoreCase)).OrderBy(p => p))))
            .OrderBy(x => x.Name)
            .ToList();

        SaveSimpleExcel(grouped, excelPath, includeVersion: true);
        return grouped;
    }

    private static List<FileEntry> ScanProductModel(string modelFilePath, string excelPath)
    {
        var xmlDoc = XDocument.Parse(File.ReadAllText(modelFilePath));

        var assemblies = xmlDoc.Descendants()
            .Where(e => e.Name.LocalName.StartsWith("Assembly", StringComparison.Ordinal))
            .Select(x => new FileEntry(
                x.Attribute("Name")?.Value ?? string.Empty,
                x.Attribute("Version")?.Value ?? string.Empty,
                QuoteJoin((x.Attribute("CopyPath")?.Value ?? string.Empty)
                    .Split(';', StringSplitOptions.RemoveEmptyEntries)
                    .Where(i => !i.Contains("Presentation", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(i => i)),
                x.Parent?.Parent?.Parent?.Attribute("Name")?.Value,
                $"{x.Parent?.Parent?.Attribute("Name")?.Value}@{x.Parent?.Name.LocalName}"))
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .Where(x => !x.Name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
            .Where(x => !x.Name.EndsWith("resources.dll", StringComparison.OrdinalIgnoreCase));

        var outputFiles = xmlDoc.Descendants()
            .Where(e => e.Name.LocalName.StartsWith("OutputFile", StringComparison.Ordinal))
            .Select(x => new FileEntry(
                (x.Value ?? string.Empty).Trim(),
                x.Parent?.Attribute("Version")?.Value ?? string.Empty,
                QuoteJoin((x.Parent?.Attribute("CopyPath")?.Value ?? string.Empty)
                    .Split(';', StringSplitOptions.RemoveEmptyEntries)
                    .Where(i => !i.Contains("Presentation", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(i => i)),
                x.Parent?.Parent?.Parent?.Parent?.Attribute("Name")?.Value,
                $"{x.Parent?.Parent?.Parent?.Attribute("Name")?.Value}@{x.Parent?.Parent?.Name.LocalName}"))
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .Where(x => !x.Name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
            .Where(x => !x.Name.EndsWith("resources.dll", StringComparison.OrdinalIgnoreCase));

        var result = assemblies.Concat(outputFiles).OrderBy(x => x.Name).ToList();
        SaveSimpleExcel(result, excelPath, includeVersion: false);
        return result;
    }

    private static List<DiffRow> Compare(List<FileEntry> buildFiles, List<FileEntry> modelFiles)
    {
        var diffs = buildFiles
            .GroupJoin(
                modelFiles,
                b => b.Name.ToLowerInvariant(),
                m => m.Name.ToLowerInvariant(),
                (b, models) => new DiffRow
                {
                    Name = b.Name,
                    CopyPathOfBuildOutput = b.CopyPath,
                    CopyPathOfProductModel = models.FirstOrDefault()?.CopyPath
                })
            .Concat(
                modelFiles
                    .Where(m => buildFiles.All(b => !string.Equals(b.Name, m.Name, StringComparison.OrdinalIgnoreCase)))
                    .Select(m => new DiffRow
                    {
                        Name = m.Name,
                        CopyPathOfBuildOutput = null,
                        CopyPathOfProductModel = m.CopyPath
                    }))
            .Where(x => x.CopyPathOfProductModel != x.CopyPathOfBuildOutput)
            .Where(x => !SkipNameSuffixes.Any(s => x.Name.EndsWith(s, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var row in diffs)
        {
            var model = modelFiles.FirstOrDefault(a =>
                a.Name == row.Name && a.CopyPath == row.CopyPathOfProductModel);
            row.Partition = model?.Partition;
            row.ComponentPart = model?.ComponentPart;
        }

        return diffs.OrderBy(x => x.Partition).ThenBy(x => x.Name).ToList();
    }

    private static void SaveDiffExcel(List<DiffRow> rows, string path)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Items");
        ws.Cell(1, 1).Value = "Name";
        ws.Cell(1, 2).Value = "CopyPath of BuildOutput";
        ws.Cell(1, 3).Value = "CopyPath of ProductModel";
        ws.Cell(1, 4).Value = "Component_Part of ProductModel";
        ws.Cell(1, 5).Value = "Partition of ProductModel";

        for (var i = 0; i < rows.Count; i++)
        {
            ws.Cell(i + 2, 1).Value = rows[i].Name;
            ws.Cell(i + 2, 2).Value = rows[i].CopyPathOfBuildOutput;
            ws.Cell(i + 2, 3).Value = rows[i].CopyPathOfProductModel;
            ws.Cell(i + 2, 4).Value = rows[i].ComponentPart;
            ws.Cell(i + 2, 5).Value = rows[i].Partition;
        }

        workbook.SaveAs(path);
    }

    private static void SaveSimpleExcel(List<FileEntry> rows, string path, bool includeVersion)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Items");
        ws.Cell(1, 1).Value = "Name";
        ws.Cell(1, 2).Value = "CopyPath";
        if (includeVersion)
            ws.Cell(1, 3).Value = "Version";

        for (var i = 0; i < rows.Count; i++)
        {
            ws.Cell(i + 2, 1).Value = rows[i].Name;
            ws.Cell(i + 2, 2).Value = rows[i].CopyPath;
            if (includeVersion)
                ws.Cell(i + 2, 3).Value = rows[i].Version;
        }

        workbook.SaveAs(path);
    }

    private static string QuoteJoin(IEnumerable<string> parts) =>
        "\"" + string.Join(';', parts) + "\"";

    private static string SafeFileVersion(string path)
    {
        try
        {
            return FileVersionInfo.GetVersionInfo(path).FileVersion ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string EnsureTrailingSlash(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;

    private static string? GetOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            BuildCopyPathComparer
            Compare build-output folder paths vs product-model CopyPath values.

            Usage:
              BuildCopyPathComparer <buildAppRoot> [--model ProductModel.xml] [--out ReportFolder]

            Example:
              BuildCopyPathComparer D:\Lab\Builds\App-net8.0 --out D:\Lab\Reports
            """);
    }

    private sealed class FileEntry
    {
        public FileEntry(string name, string version, string copyPath, string? partition = null, string? componentPart = null)
        {
            Name = name;
            Version = version;
            CopyPath = copyPath;
            Partition = partition;
            ComponentPart = componentPart;
        }

        public string Name { get; }
        public string Version { get; }
        public string CopyPath { get; }
        public string? Partition { get; }
        public string? ComponentPart { get; }
    }

    private sealed class DiffRow
    {
        public required string Name { get; set; }
        public string? CopyPathOfBuildOutput { get; set; }
        public string? CopyPathOfProductModel { get; set; }
        public string? Partition { get; set; }
        public string? ComponentPart { get; set; }
    }
}
