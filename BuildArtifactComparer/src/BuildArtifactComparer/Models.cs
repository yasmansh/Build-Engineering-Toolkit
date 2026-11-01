namespace BuildArtifactComparer;

internal sealed class AssemblyInfo
{
    public AssemblyInfo(string fileName, string relativePath, string fileVersion)
    {
        FileName = fileName;
        RelativePath = relativePath;
        FileVersion = fileVersion;
    }

    public string FileName { get; }
    public string RelativePath { get; }
    public string FileVersion { get; }
}

internal sealed class AssemblyDifference
{
    public AssemblyDifference(string fileName, string relativePath, string? fileVersion1, string? fileVersion2)
    {
        FileName = fileName;
        RelativePath = NormalizeRelativePath(relativePath);
        FileVersion1 = fileVersion1 ?? "not exists";
        FileVersion2 = fileVersion2 ?? "not exists";
    }

    public string FileName { get; }
    public string RelativePath { get; }
    public string FileVersion1 { get; }
    public string FileVersion2 { get; }

    private static string NormalizeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return "root";

        return relativePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
