namespace AzurePipelineAdmin.Models;

internal sealed class AppSettings
{
    public required string BaseUrl { get; init; }
    public required string Organization { get; init; }
    public required string Pat { get; init; }
    public string BuildToolTaskId { get; init; } = "00000000-0000-0000-0000-000000000000";
}

internal sealed class PipelineRef
{
    public required string ProjectName { get; init; }
    public required int Id { get; init; }
    public required string Name { get; init; }
}

internal sealed class TfvcMappingRoot
{
    public List<TfvcMappingEntry> mappings { get; set; } = [];
}

internal sealed class TfvcMappingEntry
{
    public string serverPath { get; set; } = "";
    public string mappingType { get; set; } = "map";
    public string localPath { get; set; } = "";
}
