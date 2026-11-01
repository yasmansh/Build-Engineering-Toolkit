using AzurePipelineAdmin.Commands;
using AzurePipelineAdmin.Models;
using AzurePipelineAdmin.Services;

namespace AzurePipelineAdmin;

/*
 * Same idea as the original one-off scripts:
 * connect to Azure DevOps -> walk projects/pipelines -> do one job.
 *
 * Set these once (recommended), then run a command:
 *   set AZDO_BASE_URL=https://dev.azure.com
 *   set AZDO_ORG=contoso
 *   set AZDO_PAT=your-token
 *   set AZDO_BUILD_TOOL_TASK_ID=your-build-tool-task-guid
 *
 * Or pass --url --org --pat --task-id on the command line.
 *
 * Examples (actions run immediately, like the old scripts):
 *   AzurePipelineAdmin report-branches
 *   AzurePipelineAdmin report-tools
 *   AzurePipelineAdmin update-tool --filter Build --selection latestRelease --framework net8.0
 *   AzurePipelineAdmin add-mapping --server $/Shared/BuildTemplates --local \BuildTemplates
 *   AzurePipelineAdmin disable --filter 19_1
 *   AzurePipelineAdmin enable --filter 19_1
 *   AzurePipelineAdmin list-busy --filter 19_0
 *   AzurePipelineAdmin delete --filter 17_1 --exclude Test --yes
 */
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintHelp();
            return args.Length == 0 ? 1 : 0;
        }

        var command = NormalizeCommand(args[0]);
        var options = ParseOptions(args.Skip(1).ToArray());

        try
        {
            var settings = LoadSettings(options);
            using var client = new AzureDevOpsClient(settings);

            switch (command)
            {
                case "report-branches":
                    await PipelineCommands.ReportToolBranchesAsync(client, settings);
                    break;

                case "report-tools":
                    await PipelineCommands.ReportToolUsageAsync(client, settings);
                    break;

                case "update-tool":
                    await PipelineCommands.UpdateBuildToolAsync(
                        client,
                        settings,
                        GetFilter(options),
                        Opt(options, "selection"),
                        Opt(options, "branch"),
                        Opt(options, "framework"),
                        Opt(options, "display-name"),
                        Opt(options, "comment"));
                    Console.WriteLine("Pipeline tasks updated successfully!");
                    break;

                case "add-mapping":
                    await PipelineCommands.AddTfvcMappingAsync(
                        client,
                        settings,
                        Require(options, "server", "server-path"),
                        Require(options, "local", "local-path"),
                        GetFilter(options),
                        HasFlag(options, "only-default-tool"),
                        Opt(options, "default-tool-branch") ?? "");
                    Console.WriteLine("Pipelines updated successfully!");
                    break;

                case "disable":
                    await PipelineCommands.SetQueueStatusAsync(
                        client,
                        settings,
                        GetFilter(options),
                        queueStatus: 2,
                        Opt(options, "comment") ?? "Pipeline disabled",
                        onlyEnabled: HasFlag(options, "only-enabled"));
                    break;

                case "enable":
                    await PipelineCommands.SetQueueStatusAsync(
                        client,
                        settings,
                        GetFilter(options),
                        queueStatus: 0,
                        Opt(options, "comment"),
                        onlyEnabled: false);
                    break;

                case "list-busy":
                    await PipelineCommands.ListBusyAsync(
                        client,
                        GetFilter(options),
                        onlyEnabled: !HasFlag(options, "all"));
                    break;

                case "delete":
                    await PipelineCommands.DeletePipelinesAsync(
                        client,
                        GetFilter(options),
                        Opt(options, "exclude", "exclude-contains"),
                        yes: HasFlag(options, "yes"));
                    break;

                default:
                    Console.WriteLine($"Unknown command: {args[0]}");
                    PrintHelp();
                    return 1;
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static string NormalizeCommand(string value) => value.ToLowerInvariant() switch
    {
        "report-tool-branches" or "report-branches" or "branches" => "report-branches",
        "report-tool-usage" or "report-tools" or "tools" => "report-tools",
        "update-build-tool" or "update-tool" or "update" => "update-tool",
        "add-tfvc-mapping" or "add-mapping" or "mapping" => "add-mapping",
        "disable" => "disable",
        "enable" => "enable",
        "list-busy" or "busy" => "list-busy",
        "delete" => "delete",
        _ => value.ToLowerInvariant()
    };

    private static AppSettings LoadSettings(Dictionary<string, string?> options)
    {
        // Same connection idea as the old scripts, but from env/flags (no hardcoded secrets).
        var baseUrl = Opt(options, "url", "base-url")
                      ?? Environment.GetEnvironmentVariable("AZDO_BASE_URL");
        var org = Opt(options, "org")
                  ?? Environment.GetEnvironmentVariable("AZDO_ORG");
        var pat = Opt(options, "pat")
                  ?? Environment.GetEnvironmentVariable("AZDO_PAT");
        var taskId = Opt(options, "task-id")
                     ?? Environment.GetEnvironmentVariable("AZDO_BUILD_TOOL_TASK_ID")
                     ?? "00000000-0000-0000-0000-000000000000";

        if (string.IsNullOrWhiteSpace(baseUrl) ||
            string.IsNullOrWhiteSpace(org) ||
            string.IsNullOrWhiteSpace(pat))
        {
            throw new InvalidOperationException(
                "Set connection first:\n" +
                "  set AZDO_BASE_URL=https://dev.azure.com\n" +
                "  set AZDO_ORG=contoso\n" +
                "  set AZDO_PAT=your-token\n" +
                "Or pass: --url ... --org ... --pat ...");
        }

        return new AppSettings
        {
            BaseUrl = baseUrl.TrimEnd('/'),
            Organization = org,
            Pat = pat,
            BuildToolTaskId = taskId
        };
    }

    private static Dictionary<string, string?> ParseOptions(string[] args)
    {
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
                continue;

            var key = arg[2..];
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                map[key] = args[++i];
            else
                map[key] = "true";
        }

        return map;
    }

    private static string? GetFilter(Dictionary<string, string?> options) =>
        Opt(options, "filter", "name-contains");

    private static string? Opt(Dictionary<string, string?> options, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (options.TryGetValue(key, out var value))
                return value;
        }

        return null;
    }

    private static string Require(Dictionary<string, string?> options, params string[] keys) =>
        Opt(options, keys) ?? throw new InvalidOperationException($"Missing option --{keys[0]}");

    private static bool HasFlag(Dictionary<string, string?> options, string key) =>
        options.TryGetValue(key, out var value) &&
        (value is null || value.Equals("true", StringComparison.OrdinalIgnoreCase));

    private static bool IsHelp(string value) =>
        value is "-h" or "--help" or "help" or "/?";

    private static void PrintHelp()
    {
        Console.WriteLine("""
            AzurePipelineAdmin
            One tool for the jobs you used to run as separate scripts.

            1) Set connection (once per terminal):
                 set AZDO_BASE_URL=https://dev.azure.com
                 set AZDO_ORG=contoso
                 set AZDO_PAT=your-token
                 set AZDO_BUILD_TOOL_TASK_ID=your-task-guid

            2) Run a command (it does the action immediately):

            REPORT
              report-branches
                  List build-tool branches used by pipelines
              report-tools
                  Group pipelines by build-tool (branch / latestRelease)
              list-busy --filter 19_0
                  Pipelines that have builds in progress or queued

            UPDATE (like your old PUT scripts)
              update-tool --filter Build --selection latestRelease --framework net8.0
              update-tool --filter Build --selection latestPrerelease --branch my-tool-branch --framework net8.0
              add-mapping --server $/Shared/BuildTemplates --local \BuildTemplates
              disable --filter 19_1
              enable --filter 19_1

            DELETE
              delete --filter 17_1 --exclude Test --yes

            Tips
              --filter TEXT     pipeline name contains TEXT (same idea as your if Contains(...))
              --exclude TEXT    skip names containing TEXT (delete)
              --yes             required for delete
              --url --org --pat can replace env vars for one run
            """);
    }
}
