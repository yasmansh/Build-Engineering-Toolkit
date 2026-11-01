using System.Diagnostics;
using ClosedXML.Excel;

namespace BuildArtifactComparer;

/*
 * Compares two build output folders by assembly FileVersion (not binary content).
 * Scans *.dll files, skips *.resources.dll, and skips configurable vendor prefixes.
 *
 * Usage:
 *   BuildArtifactComparer <dir1> <dir2> [--sheet SheetName] [--out OutputFolder]
 *
 * Example:
 *   BuildArtifactComparer D:\Lab\Builds\BuildA\App-net8.0 D:\Lab\Builds\BuildB\App-net8.0 --sheet net8.0
 */
internal static class Program
{
    // Lab defaults (generic paths — replace with your build folders)
    private static string _appDir1 = @"D:\Lab\Builds\BuildA\App-net8.0";
    private static string _appDir2 = @"D:\Lab\Builds\BuildB\App-net8.0";
    private static string _sheetName = "net8.0";
    private static string _savePath = ""; // empty => current working directory

    // Skip satellite resource assemblies and optional first-party prefixes
    private static readonly string[] ExcludedFileNamePrefixes =
    [
        // e.g. "Company", "InternalProduct"
    ];

    private static int Main(string[] args)
    {
        if (!TryParseArgs(args))
        {
            PrintUsage();
            return 1;
        }

        if (!Directory.Exists(_appDir1) || !Directory.Exists(_appDir2))
        {
            Console.Error.WriteLine("One or both input directories do not exist.");
            Console.Error.WriteLine($"  dir1: {_appDir1}");
            Console.Error.WriteLine($"  dir2: {_appDir2}");
            return 1;
        }

        CompareAssemblies(_appDir1, _appDir2);
        return 0;
    }

    private static bool TryParseArgs(string[] args)
    {
        if (args.Length == 0)
            return true; // use defaults for local lab runs

        if (args.Length < 2)
            return false;

        _appDir1 = args[0];
        _appDir2 = args[1];

        for (var i = 2; i < args.Length; i++)
        {
            if (args[i] is "--sheet" && i + 1 < args.Length)
            {
                _sheetName = args[++i];
                continue;
            }

            if (args[i] is "--out" && i + 1 < args.Length)
            {
                _savePath = args[++i];
                continue;
            }

            return false;
        }

        return true;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  BuildArtifactComparer <dir1> <dir2> [--sheet SheetName] [--out OutputFolder]");
        Console.WriteLine();
        Console.WriteLine("Compares DLL FileVersion values between two build output trees.");
        Console.WriteLine("Skips *.resources.dll. Optional vendor prefixes can be configured in code.");
    }

    private static void CompareAssemblies(string appDir1, string appDir2)
    {
        var assemblies1 = GetAssemblyList(appDir1);
        var assemblies2 = GetAssemblyList(appDir2);
        var result = new List<AssemblyDifference>();

        foreach (var file1 in assemblies1)
        {
            var file2 = assemblies2.FirstOrDefault(x =>
                x.FileName == file1.FileName &&
                x.RelativePath == file1.RelativePath);

            if (file2 is not null)
            {
                if (file1.FileVersion == file2.FileVersion)
                    continue;

                result.Add(new AssemblyDifference(
                    file1.FileName,
                    file1.RelativePath,
                    file1.FileVersion,
                    file2.FileVersion));
            }
            else
            {
                result.Add(new AssemblyDifference(
                    file1.FileName,
                    file1.RelativePath,
                    file1.FileVersion,
                    null));
            }
        }

        foreach (var file2 in assemblies2)
        {
            var file1 = assemblies1.FirstOrDefault(x =>
                x.FileName == file2.FileName &&
                x.RelativePath == file2.RelativePath);

            if (file1 is null)
            {
                result.Add(new AssemblyDifference(
                    file2.FileName,
                    file2.RelativePath,
                    null,
                    file2.FileVersion));
            }
        }

        SaveToExcel(result);
    }

    private static List<AssemblyInfo> GetAssemblyList(string appDir)
    {
        return Directory
            .EnumerateFiles(appDir, "*.dll", SearchOption.AllDirectories)
            .Where(IsComparableDll)
            .Select(path => new AssemblyInfo(
                Path.GetFileName(path),
                Path.GetDirectoryName(Path.GetRelativePath(appDir, path)) ?? string.Empty,
                GetFileVersion(path)))
            .OrderBy(x => x.FileName)
            .ThenBy(x => x.RelativePath)
            .ToList();
    }

    private static bool IsComparableDll(string path)
    {
        var fileName = Path.GetFileName(path);

        if (fileName.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase))
            return false;

        foreach (var prefix in ExcludedFileNamePrefixes)
        {
            if (!string.IsNullOrWhiteSpace(prefix) &&
                fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static string GetFileVersion(string filePath)
    {
        try
        {
            var fileInfo = FileVersionInfo.GetVersionInfo(filePath);
            return !string.IsNullOrEmpty(fileInfo.FileVersion) ? fileInfo.FileVersion : "N/A";
        }
        catch
        {
            return "N/A";
        }
    }

    private static void SaveToExcel(List<AssemblyDifference> result)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.AddWorksheet(_sheetName);
        ws.Cell(1, 1).InsertTable(result);

        var fileName = $"result-{DateTime.Now:yy-MM-dd-HH-mm}.xlsx";
        var directory = string.IsNullOrWhiteSpace(_savePath)
            ? Directory.GetCurrentDirectory()
            : _savePath;

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        workbook.SaveAs(path);
        Console.WriteLine($"Result successfully saved to {path}");
        Console.WriteLine($"Differences found: {result.Count}");
    }
}
