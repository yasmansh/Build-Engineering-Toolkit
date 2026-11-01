using System.Net.Http.Headers;
using System.Text;
using AzurePipelineAdmin.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AzurePipelineAdmin.Services;

internal sealed class AzureDevOpsClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly AppSettings _settings;

    public AzureDevOpsClient(AppSettings settings)
    {
        _settings = settings;
        _http = new HttpClient();
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{settings.Pat}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
    }

    public async Task<IReadOnlyList<string>> GetProjectNamesAsync()
    {
        var url = $"{_settings.BaseUrl}/{_settings.Organization}/_apis/projects?api-version=6.0";
        var json = await _http.GetStringAsync(url);
        var doc = JObject.Parse(json);
        return doc["value"]?
            .Select(p => (string?)p["name"])
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Cast<string>()
            .ToList()
            ?? [];
    }

    public async Task<IReadOnlyList<PipelineRef>> GetPipelinesAsync(string projectName)
    {
        var url = $"{_settings.BaseUrl}/{_settings.Organization}/{Uri.EscapeDataString(projectName)}/_apis/build/definitions?api-version=6.0";
        var json = await _http.GetStringAsync(url);
        var doc = JObject.Parse(json);
        return doc["value"]?
            .Select(p => new PipelineRef
            {
                ProjectName = projectName,
                Id = (int)p["id"]!,
                Name = (string)p["name"]!
            })
            .ToList()
            ?? [];
    }

    public async Task<JObject> GetDefinitionAsync(string projectName, int pipelineId)
    {
        var url = DefinitionUrl(projectName, pipelineId);
        var json = await _http.GetStringAsync(url);
        return JObject.Parse(json);
    }

    public async Task UpdateDefinitionAsync(string projectName, int pipelineId, JObject definition)
    {
        var url = DefinitionUrl(projectName, pipelineId);
        var content = new StringContent(definition.ToString(Formatting.None), Encoding.UTF8, "application/json");
        var response = await _http.PutAsync(url, content);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteDefinitionAsync(string projectName, int pipelineId)
    {
        var url = DefinitionUrl(projectName, pipelineId);
        var response = await _http.DeleteAsync(url);
        response.EnsureSuccessStatusCode();
    }

    public async Task<int> GetBusyBuildCountAsync(string projectName, int pipelineId)
    {
        var url =
            $"{_settings.BaseUrl}/{_settings.Organization}/{Uri.EscapeDataString(projectName)}" +
            $"/_apis/build/builds?definitions={pipelineId}&statusFilter=inProgress,notStarted&api-version=6.0";
        var json = await _http.GetStringAsync(url);
        var doc = JObject.Parse(json);
        return (int?)doc["count"] ?? 0;
    }

    public string DefinitionWebLink(string projectName, int pipelineId) =>
        $"{_settings.BaseUrl}/{_settings.Organization}/{Uri.EscapeDataString(projectName)}/_build?definitionId={pipelineId}";

    private string DefinitionUrl(string projectName, int pipelineId) =>
        $"{_settings.BaseUrl}/{_settings.Organization}/{Uri.EscapeDataString(projectName)}/_apis/build/definitions/{pipelineId}?api-version=6.0";

    public void Dispose() => _http.Dispose();
}

internal static class BuildToolStepHelper
{
    public static IEnumerable<JToken> EnumerateBuildToolSteps(JObject definition, string taskId)
    {
        var phases = definition["process"]?["phases"];
        if (phases is null)
            yield break;

        foreach (var phase in phases)
        {
            var steps = phase["steps"];
            if (steps is null)
                continue;

            foreach (var step in steps)
            {
                var id = (string?)step["task"]?["id"];
                if (string.Equals(id, taskId, StringComparison.OrdinalIgnoreCase))
                    yield return step;
            }
        }
    }
}
