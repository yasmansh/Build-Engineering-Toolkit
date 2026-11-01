using AzurePipelineAdmin.Models;
using AzurePipelineAdmin.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AzurePipelineAdmin.Commands;

internal static class PipelineCommands
{
    public static async Task ReportToolBranchesAsync(AzureDevOpsClient client, AppSettings settings)
    {
        var branches = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in await client.GetProjectNamesAsync())
        {
            foreach (var pipeline in await client.GetPipelinesAsync(project))
            {
                var definition = await client.GetDefinitionAsync(project, pipeline.Id);
                foreach (var step in BuildToolStepHelper.EnumerateBuildToolSteps(definition, settings.BuildToolTaskId))
                {
                    var branch = (string?)step["inputs"]?["btVersionBranch"];
                    if (!string.IsNullOrWhiteSpace(branch))
                        branches.Add(branch);
                }
            }
        }

        Console.WriteLine("Build tool branches in use:");
        foreach (var branch in branches)
            Console.WriteLine(branch);
    }

    public static async Task ReportToolUsageAsync(AzureDevOpsClient client, AppSettings settings)
    {
        var groups = new Dictionary<string, List<(string Name, string Link)>>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in await client.GetProjectNamesAsync())
        {
            foreach (var pipeline in await client.GetPipelinesAsync(project))
            {
                var definition = await client.GetDefinitionAsync(project, pipeline.Id);
                foreach (var step in BuildToolStepHelper.EnumerateBuildToolSteps(definition, settings.BuildToolTaskId))
                {
                    var selection = (string?)step["inputs"]?["btVersionSelection"] ?? "";
                    var branch = (string?)step["inputs"]?["btVersionBranch"] ?? "";
                    var key = string.Equals(selection, "latestRelease", StringComparison.OrdinalIgnoreCase)
                        ? "latestRelease"
                        : (string.IsNullOrWhiteSpace(branch) ? selection : branch);

                    if (!groups.TryGetValue(key, out var list))
                    {
                        list = [];
                        groups[key] = list;
                    }

                    list.Add((pipeline.Name, client.DefinitionWebLink(project, pipeline.Id)));
                }
            }
        }

        foreach (var group in groups.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine($"\nBuild tool: {group.Key}");
            foreach (var item in group.Value.OrderBy(x => x.Name))
                Console.WriteLine($"  {item.Name}\t{item.Link}");
        }
    }

    public static async Task UpdateBuildToolAsync(
        AzureDevOpsClient client,
        AppSettings settings,
        string? nameContains,
        string? selection,
        string? branch,
        string? framework,
        string? displayName,
        string? comment)
    {
        var updated = new List<string>();

        foreach (var project in await client.GetProjectNamesAsync())
        {
            foreach (var pipeline in await client.GetPipelinesAsync(project))
            {
                if (!MatchesName(pipeline.Name, nameContains))
                    continue;

                var definition = await client.GetDefinitionAsync(project, pipeline.Id);
                var changed = false;

                foreach (var step in BuildToolStepHelper.EnumerateBuildToolSteps(definition, settings.BuildToolTaskId))
                {
                    var inputs = step["inputs"] as JObject ?? new JObject();
                    step["inputs"] = inputs;

                    if (selection is not null)
                        inputs["btVersionSelection"] = selection;
                    if (branch is not null)
                        inputs["btVersionBranch"] = branch;
                    if (framework is not null)
                        inputs["btFramework"] = framework;
                    if (displayName is not null)
                        step["displayName"] = displayName;

                    changed = true;
                }

                if (!changed)
                    continue;

                if (comment is not null)
                    definition["comment"] = comment;

                await client.UpdateDefinitionAsync(project, pipeline.Id, definition);
                updated.Add($"{project}/{pipeline.Name}");
            }
        }

        PrintResult("Updated pipelines", updated);
    }

    public static async Task AddTfvcMappingAsync(
        AzureDevOpsClient client,
        AppSettings settings,
        string serverPath,
        string localPath,
        string? nameContains,
        bool onlyWhenToolBranchEmptyOrDefault,
        string? defaultToolBranch)
    {
        var updated = new List<string>();

        foreach (var project in await client.GetProjectNamesAsync())
        {
            foreach (var pipeline in await client.GetPipelinesAsync(project))
            {
                if (!MatchesName(pipeline.Name, nameContains))
                    continue;

                var definition = await client.GetDefinitionAsync(project, pipeline.Id);
                var steps = BuildToolStepHelper.EnumerateBuildToolSteps(definition, settings.BuildToolTaskId).ToList();
                if (steps.Count == 0)
                    continue;

                if (onlyWhenToolBranchEmptyOrDefault)
                {
                    var ok = steps.Any(step =>
                    {
                        var branch = (string?)step["inputs"]?["btVersionBranch"] ?? "";
                        return string.IsNullOrWhiteSpace(branch) ||
                               string.Equals(branch, defaultToolBranch, StringComparison.OrdinalIgnoreCase);
                    });
                    if (!ok)
                        continue;
                }

                var mappingToken = definition["repository"]?["properties"]?["tfvcMapping"];
                if (mappingToken is null)
                    continue;

                var mappingJson = mappingToken.Type == JTokenType.String
                    ? (string?)mappingToken
                    : mappingToken.ToString(Formatting.None);

                if (string.IsNullOrWhiteSpace(mappingJson))
                    continue;

                var mapping = JsonConvert.DeserializeObject<TfvcMappingRoot>(mappingJson!) ?? new TfvcMappingRoot();
                mapping.mappings ??= [];

                if (mapping.mappings.Any(m =>
                        string.Equals(m.serverPath, serverPath, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                mapping.mappings.Add(new TfvcMappingEntry
                {
                    serverPath = serverPath,
                    mappingType = "map",
                    localPath = localPath
                });

                definition["repository"]!["properties"]!["tfvcMapping"] =
                    JsonConvert.SerializeObject(mapping, Formatting.Indented);

                await client.UpdateDefinitionAsync(project, pipeline.Id, definition);
                updated.Add($"{project}/{pipeline.Name}");
            }
        }

        PrintResult("Updated pipelines (mapping added)", updated);
    }

    public static async Task SetQueueStatusAsync(
        AzureDevOpsClient client,
        AppSettings settings,
        string? nameContains,
        int queueStatus,
        string? comment,
        bool onlyEnabled)
    {
        var updated = new List<string>();

        foreach (var project in await client.GetProjectNamesAsync())
        {
            foreach (var pipeline in await client.GetPipelinesAsync(project))
            {
                if (!MatchesName(pipeline.Name, nameContains))
                    continue;

                var definition = await client.GetDefinitionAsync(project, pipeline.Id);
                var current = definition["queueStatus"]?.ToString() ?? "";

                if (onlyEnabled &&
                    !string.Equals(current, "enabled", StringComparison.OrdinalIgnoreCase) &&
                    current != "0")
                {
                    continue;
                }

                definition["queueStatus"] = queueStatus;
                if (comment is not null)
                    definition["comment"] = comment;

                await client.UpdateDefinitionAsync(project, pipeline.Id, definition);
                updated.Add($"{project}/{pipeline.Name}");
            }
        }

        var action = queueStatus == 0 ? "Enabled pipelines" : "Disabled pipelines";
        PrintResult(action, updated);
    }

    public static async Task ListBusyAsync(
        AzureDevOpsClient client,
        string? nameContains,
        bool onlyEnabled)
    {
        foreach (var project in await client.GetProjectNamesAsync())
        {
            foreach (var pipeline in await client.GetPipelinesAsync(project))
            {
                if (!MatchesName(pipeline.Name, nameContains))
                    continue;

                var definition = await client.GetDefinitionAsync(project, pipeline.Id);
                var status = definition["queueStatus"]?.ToString() ?? "";
                if (onlyEnabled &&
                    !string.Equals(status, "enabled", StringComparison.OrdinalIgnoreCase) &&
                    status != "0")
                {
                    continue;
                }

                var count = await client.GetBusyBuildCountAsync(project, pipeline.Id);
                if (count > 0)
                    Console.WriteLine($"{pipeline.Name}\tbusy={count}\t{project}");
            }
        }
    }

    public static async Task DeletePipelinesAsync(
        AzureDevOpsClient client,
        string? nameContains,
        string? excludeContains,
        bool yes)
    {
        if (!yes)
        {
            Console.WriteLine("Delete will permanently remove pipelines.");
            Console.WriteLine("Run again with --yes if you are sure.");
            Console.WriteLine("Example: delete --filter 17_1 --exclude Test --yes");
            return;
        }

        var deleted = new List<string>();

        foreach (var project in await client.GetProjectNamesAsync())
        {
            foreach (var pipeline in await client.GetPipelinesAsync(project))
            {
                if (!MatchesName(pipeline.Name, nameContains))
                    continue;
                if (!string.IsNullOrWhiteSpace(excludeContains) &&
                    pipeline.Name.Contains(excludeContains, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                await client.DeleteDefinitionAsync(project, pipeline.Id);
                deleted.Add($"{project}/{pipeline.Name}");
            }
        }

        PrintResult("Deleted pipelines", deleted);
    }

    private static bool MatchesName(string pipelineName, string? nameContains) =>
        string.IsNullOrWhiteSpace(nameContains) ||
        pipelineName.Contains(nameContains, StringComparison.OrdinalIgnoreCase);

    private static void PrintResult(string action, List<string> items)
    {
        Console.WriteLine($"\n{action}: {items.Count}");
        foreach (var item in items)
            Console.WriteLine(item);
        if (items.Count == 0)
            Console.WriteLine("(none matched your filter)");
    }
}
