# Build Artifact Comparer

Console tool that compares **two .NET build output folders** by assembly **FileVersion** (not binary content) and writes differences to an Excel report.

## What it does

1. Scans both directories recursively for `*.dll`
2. Skips `*.resources.dll` (satellite / localization assemblies)
3. Optionally skips DLLs whose file names start with configured vendor prefixes
4. Matches assemblies by **file name + relative path**
5. Reports:
   - version changed between build A and build B
   - present only in build A
   - present only in build B
6. Exports the result table to `.xlsx` via ClosedXML

```text
BuildA/App-net8.0/**/*.dll  ──┐
                              ├── FileVersion compare ──► result-YY-MM-DD-HH-mm.xlsx
BuildB/App-net8.0/**/*.dll  ──┘
```

## Why FileVersion (not binary hash)

Useful when you need a **fast release/build diff**: which third-party or shared libraries changed version between two build drops (for example maintenance vs release packages), without a full binary compare.

## Requirements

- .NET 8 SDK
- Two local build output folders to compare

## Run

```bash
cd src/BuildArtifactComparer
dotnet run -- "D:\Lab\Builds\BuildA\App-net8.0" "D:\Lab\Builds\BuildB\App-net8.0" --sheet net8.0 --out "D:\Lab\Reports"
```

| Argument | Meaning |
|---|---|
| `dir1` | First build output root |
| `dir2` | Second build output root |
| `--sheet` | Excel sheet name (default: `net8.0`) |
| `--out` | Folder for the Excel file (default: current directory) |

With no arguments, the tool uses the lab default paths defined in `Program.cs`.

## Output columns

| Column | Description |
|---|---|
| `FileName` | DLL name |
| `RelativePath` | Folder under the app root (`root` if at top level) |
| `FileVersion1` | Version in dir1, or `not exists` |
| `FileVersion2` | Version in dir2, or `not exists` |

## Configuration notes

- Excluded name prefixes live in `ExcludedFileNamePrefixes` in `Program.cs` (empty by default in this sample).
- Satellite assemblies ending with `.resources.dll` are always skipped.
- This sample uses **generic paths and names only** — no real product folders, tokens, or internal prefixes.

## Project layout

```text
portfolio-build-compare/
├── README.md
├── INTERVIEW.md
└── src/
    ├── BuildArtifactComparer.sln
    └── BuildArtifactComparer/
        ├── BuildArtifactComparer.csproj
        ├── Program.cs
        └── Models.cs
```

## Stack

- C# / .NET 8
- ClosedXML (Excel export)
