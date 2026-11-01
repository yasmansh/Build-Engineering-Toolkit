using System.Reflection;

namespace AssemblyDependencyChain;

/*
 * Finds A -> B -> C reference chains among assemblies in a build output folder.
 *
 * Usage:
 *   AssemblyDependencyChain <rootPath>
 *       [--include Prefix.]
 *       [--exclude PrefixToSkip.]
 *       [--depth 3]
 *
 * Example:
 *   AssemblyDependencyChain D:\Lab\Builds\App-net8.0 --include Company. --exclude Company.Shared.
 */
internal static class Program
{
    private static string _rootPath = @"D:\Lab\Builds\App-net8.0";
    private static string _includePrefix = "Company.";
    private static string _excludePrefix = "Company.Shared.";
    private static int _chainDepth = 3;

    private static int Main(string[] args)
    {
        if (!TryParseArgs(args))
        {
            PrintUsage();
            return 1;
        }

        if (!Directory.Exists(_rootPath))
        {
            Console.Error.WriteLine($"Root path does not exist: {_rootPath}");
            return 1;
        }

        if (_chainDepth < 2)
        {
            Console.Error.WriteLine("Depth must be at least 2.");
            return 1;
        }

        var assemblyMap = LoadAssemblyMap(_rootPath, _includePrefix, _excludePrefix);
        var references = BuildReferenceGraph(assemblyMap, _excludePrefix);
        var chains = FindChains(references, _chainDepth);

        foreach (var chain in chains)
            Console.WriteLine(FormatChain(chain));

        Console.WriteLine($"Assemblies scanned: {assemblyMap.Count}");
        Console.WriteLine($"Chains found (depth {_chainDepth}): {chains.Count}");
        return 0;
    }

    private static bool TryParseArgs(string[] args)
    {
        if (args.Length == 0)
            return true;

        _rootPath = args[0];

        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] is "--include" && i + 1 < args.Length)
            {
                _includePrefix = args[++i];
                continue;
            }

            if (args[i] is "--exclude" && i + 1 < args.Length)
            {
                _excludePrefix = args[++i];
                continue;
            }

            if (args[i] is "--depth" && i + 1 < args.Length && int.TryParse(args[i + 1], out var depth))
            {
                _chainDepth = depth;
                i++;
                continue;
            }

            return false;
        }

        return true;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  AssemblyDependencyChain <rootPath> [--include Prefix.] [--exclude PrefixToSkip.] [--depth 3]");
        Console.WriteLine();
        Console.WriteLine("Finds assembly reference chains (A -> B -> C ...) inside a build output folder.");
    }

    private static Dictionary<string, string> LoadAssemblyMap(
        string rootPath,
        string includePrefix,
        string excludePrefix)
    {
        var pattern = string.IsNullOrWhiteSpace(includePrefix)
            ? "*.dll"
            : $"{includePrefix}*.dll";

        var files = Directory.GetFiles(rootPath, pattern, SearchOption.AllDirectories)
            .Where(f =>
            {
                var name = Path.GetFileName(f);
                if (name.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase))
                    return false;

                if (!string.IsNullOrWhiteSpace(excludePrefix) &&
                    name.StartsWith(excludePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return true;
            });

        var assemblyMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            try
            {
                var name = AssemblyName.GetAssemblyName(file).Name;
                if (!string.IsNullOrWhiteSpace(name))
                    assemblyMap[name] = file;
            }
            catch
            {
                // Skip non-managed or unloadable binaries
            }
        }

        return assemblyMap;
    }

    private static Dictionary<string, List<string>> BuildReferenceGraph(
        Dictionary<string, string> assemblyMap,
        string excludePrefix)
    {
        var references = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (assemblyName, filePath) in assemblyMap)
        {
            try
            {
                var assembly = Assembly.LoadFrom(filePath);
                var referenced = assembly.GetReferencedAssemblies()
                    .Select(r => r.Name)
                    .Where(r =>
                        !string.IsNullOrWhiteSpace(r) &&
                        assemblyMap.ContainsKey(r!) &&
                        (string.IsNullOrWhiteSpace(excludePrefix) ||
                         !r!.StartsWith(excludePrefix, StringComparison.OrdinalIgnoreCase)))
                    .Cast<string>()
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                references[assemblyName] = referenced;
            }
            catch
            {
                references[assemblyName] = [];
            }
        }

        return references;
    }

    private static List<List<string>> FindChains(
        Dictionary<string, List<string>> references,
        int depth)
    {
        var results = new List<List<string>>();

        foreach (var start in references.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            Walk(start, [start], references, depth, results);

        return results;
    }

    private static void Walk(
        string current,
        List<string> path,
        Dictionary<string, List<string>> references,
        int depth,
        List<List<string>> results)
    {
        if (path.Count == depth)
        {
            results.Add([.. path]);
            return;
        }

        if (!references.TryGetValue(current, out var nextList))
            return;

        foreach (var next in nextList)
        {
            if (path.Contains(next, StringComparer.OrdinalIgnoreCase))
                continue; // avoid cycles in the printed chain

            if (!references.ContainsKey(next))
                continue;

            path.Add(next);
            Walk(next, path, references, depth, results);
            path.RemoveAt(path.Count - 1);
        }
    }

    private static string FormatChain(List<string> chain)
    {
        var lines = new List<string>();
        for (var i = 0; i < chain.Count; i++)
        {
            var indent = new string(' ', i * 2);
            var arrow = i == 0 ? string.Empty : "-> ";
            lines.Add($"{indent}{arrow}{chain[i]}.dll");
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }
}
