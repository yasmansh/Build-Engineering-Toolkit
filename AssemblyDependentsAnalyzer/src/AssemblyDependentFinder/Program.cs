using System.Reflection;
using System.Xml.Linq;

namespace AssemblyDependentFinder;

/*
 * Finds who depends on a given assembly:
 *  1) DLLs in a build output that reference it
 *  2) .csproj files under source roots that mention it
 *
 * Useful when a DLL differs between two builds and you need blast radius.
 *
 * Usage:
 *   AssemblyDependentFinder <buildAppRoot> <productModelXml> --src Root1 --src Root2
 *   Then type an assembly name interactively (or pass --assembly Name once).
 *
 * Example:
 *   AssemblyDependentFinder D:\Lab\Builds\App-net8.0 D:\Lab\Builds\App-net8.0\ComponentModel\ProductModel.xml --src D:\Src\ProductA\Main
 */
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2 || args[0] is "-h" or "--help")
        {
            PrintHelp();
            return args.Length < 2 ? 1 : 0;
        }

        var buildRoot = args[0];
        var modelPath = args[1];
        var sourceRoots = GetOptions(args, "--src");
        var oneShotAssembly = GetOption(args, "--assembly");
        var srcRootReplaceFrom = GetOption(args, "--map-from") ?? "$";
        var srcRootReplaceTo = GetOption(args, "--map-to") ?? @"D:\Src";

        if (!Directory.Exists(buildRoot))
        {
            Console.Error.WriteLine($"Build folder not found: {buildRoot}");
            return 1;
        }

        if (!File.Exists(modelPath))
        {
            Console.Error.WriteLine($"Product model not found: {modelPath}");
            return 1;
        }

        Console.WriteLine("Loading build DLLs...");
        var buildDlls = LoadBuildDlls(buildRoot);
        Console.WriteLine($"Loaded assemblies: {buildDlls.Count}");

        Console.WriteLine("Loading product model...");
        var model = LoadProductModel(modelPath, srcRootReplaceFrom, srcRootReplaceTo);
        Console.WriteLine($"Model assemblies: {model.Count}");

        Console.WriteLine("Indexing csproj files...");
        var csprojFiles = IndexCsprojFiles(sourceRoots, model);
        Console.WriteLine($"csproj files: {csprojFiles.Count}");

        if (!string.IsNullOrWhiteSpace(oneShotAssembly))
        {
            PrintReport(oneShotAssembly, buildDlls, model, csprojFiles);
            return 0;
        }

        var assemblyName = "Some.ThirdParty.Library";
        while (true)
        {
            Console.Clear();
            PrintReport(assemblyName, buildDlls, model, csprojFiles);
            Console.WriteLine("Enter assembly name (or 'exit'):");
            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input))
                continue;
            if (input.Equals("exit", StringComparison.OrdinalIgnoreCase))
                break;
            assemblyName = input.Trim();
        }

        return 0;
    }

    private static void PrintReport(
        string assemblyName,
        List<LoadedDll> buildDlls,
        Dictionary<string, ModelInfo> model,
        List<CsprojInfo> csprojFiles)
    {
        Console.WriteLine("Assembly Name: " + assemblyName);
        Console.WriteLine("\n~~~~~~~~~~~~ DLL references in build output ~~~~~~~~~~~~");
        foreach (var dll in buildDlls)
        {
            try
            {
                if (!dll.Assembly.GetReferencedAssemblies()
                        .Any(r => string.Equals(r.Name, assemblyName, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var copyPath = model.TryGetValue(dll.Name, out var info) ? info.CopyPath : "-";
                Console.WriteLine($"{dll.Name}\tCopyPath: {copyPath}");
            }
            catch
            {
                // skip unloadable reference graphs
            }
        }

        Console.WriteLine("\n~~~~~~~~~~~~ Direct usage in source .csproj files ~~~~~~~~~~~~");
        foreach (var csproj in csprojFiles)
        {
            if (!csproj.Content.Contains(assemblyName + ".dll", StringComparison.OrdinalIgnoreCase) &&
                !csproj.Content.Contains(assemblyName + "\"", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var copyPath = csproj.InProductModel
                ? model.FirstOrDefault(x => x.Value.BuildProjects.Any(p =>
                    p.Contains(csproj.FilePath, StringComparison.OrdinalIgnoreCase))).Value?.CopyPath ?? "-"
                : "-";

            Console.WriteLine($"{csproj.FilePath}\tInProductModel:{csproj.InProductModel}\tCopyPath: {copyPath}");
        }

        Console.WriteLine("\n-------------------------");
    }

    private static List<LoadedDll> LoadBuildDlls(string buildRoot)
    {
        var result = new List<LoadedDll>();
        foreach (var dll in Directory.GetFiles(buildRoot, "*.dll", SearchOption.AllDirectories))
        {
            if (dll.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                result.Add(new LoadedDll
                {
                    Name = Path.GetFileName(dll),
                    Assembly = Assembly.LoadFrom(dll)
                });
            }
            catch
            {
                // skip native / unloadable binaries
            }
        }

        return result;
    }

    private static Dictionary<string, ModelInfo> LoadProductModel(
        string modelPath,
        string mapFrom,
        string mapTo)
    {
        var assemblies = XElement.Load(modelPath).Descendants()
            .Where(e => e.Name.LocalName == "Assembly")
            .Select(a => new
            {
                Name = (string?)a.Attribute("Name") ?? string.Empty,
                CopyPath = (string?)a.Attribute("CopyPath") ?? string.Empty,
                BuildProject = ((string?)a.Attribute("BuildProject") ?? string.Empty)
                    .Replace("/", "\\")
                    .Replace(mapFrom, mapTo)
            })
            .Where(a => !string.IsNullOrWhiteSpace(a.Name))
            .GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => new ModelInfo
                {
                    CopyPath = string.Join(";", g.Select(a => a.CopyPath).Distinct()),
                    BuildProjects = g.Select(a => a.BuildProject).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToList()
                },
                StringComparer.OrdinalIgnoreCase);

        return assemblies;
    }

    private static List<CsprojInfo> IndexCsprojFiles(
        IReadOnlyList<string> sourceRoots,
        Dictionary<string, ModelInfo> model)
    {
        if (sourceRoots.Count == 0)
            return [];

        return sourceRoots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.GetFiles(root, "*.csproj", SearchOption.AllDirectories))
            .Select(file =>
            {
                var content = File.ReadAllText(file);
                var inModel = model.Values.Any(m =>
                    m.BuildProjects.Any(p => p.Contains(file, StringComparison.OrdinalIgnoreCase)));
                return new CsprojInfo
                {
                    FilePath = file,
                    Content = content,
                    InProductModel = inModel
                };
            })
            .ToList();
    }

    private static string? GetOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }

    private static List<string> GetOptions(string[] args, string name)
    {
        var list = new List<string>();
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                list.Add(args[i + 1]);
        }

        return list;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            AssemblyDependentFinder
            Find build DLLs and csproj files that depend on a given assembly.

            Usage:
              AssemblyDependentFinder <buildAppRoot> <productModelXml> [--src SourceRoot]... [--assembly Name]

            Example:
              AssemblyDependentFinder D:\Lab\Builds\App-net8.0 D:\Lab\Builds\App-net8.0\ComponentModel\ProductModel.xml --src D:\Src\App\Main --assembly Newtonsoft.Json
            """);
    }

    private sealed class LoadedDll
    {
        public required string Name { get; init; }
        public required Assembly Assembly { get; init; }
    }

    private sealed class ModelInfo
    {
        public required string CopyPath { get; init; }
        public required List<string> BuildProjects { get; init; }
    }

    private sealed class CsprojInfo
    {
        public required string FilePath { get; init; }
        public required string Content { get; init; }
        public required bool InProductModel { get; init; }
    }
}
