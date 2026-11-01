# Azure Pipeline Admin

One .NET console app that combines your separate Azure Pipeline scripts into simple commands.

Same flow as before: connect → read classic build definitions → update / report / disable / delete.

## Quick start

```bat
set AZDO_BASE_URL=https://dev.azure.com
set AZDO_ORG=contoso
set AZDO_PAT=your-token
set AZDO_BUILD_TOOL_TASK_ID=your-build-tool-task-guid

cd src\AzurePipelineAdmin
dotnet run -- report-branches
```

Commands **do the action immediately** (like your old `Program.cs` scripts).

## Commands

| Command | What it does (same as your old scripts) |
|---|---|
| `report-branches` | Print distinct build-tool branch values |
| `report-tools` | Group pipelines by build-tool selection/branch |
| `update-tool` | Change build-tool installer step (selection / branch / framework) |
| `add-mapping` | Add a TFVC mapping on matching pipelines |
| `disable` | Set `queueStatus = 2` |
| `enable` | Set `queueStatus = 0` |
| `list-busy` | Pipelines with builds in progress or queued |
| `delete` | Delete matching pipelines (needs `--yes`) |

## Examples

```bat
REM reports
dotnet run -- report-branches
dotnet run -- report-tools
dotnet run -- list-busy --filter 19_0

REM update build tool step
dotnet run -- update-tool --filter Build --selection latestRelease --framework net8.0
dotnet run -- update-tool --filter Build --selection latestPrerelease --branch my-tool-branch

REM TFVC mapping
dotnet run -- add-mapping --server $/Shared/BuildTemplates --local \BuildTemplates

REM enable / disable
dotnet run -- disable --filter 19_1
dotnet run -- enable --filter 19_1

REM delete (only with --yes)
dotnet run -- delete --filter 17_1 --exclude Test --yes
```

`--filter` = pipeline name contains this text (same idea as `pipelineName.Contains(...)` in your scripts).

## Connection

Prefer environment variables (no secrets in code):

- `AZDO_BASE_URL`
- `AZDO_ORG`
- `AZDO_PAT`
- `AZDO_BUILD_TOOL_TASK_ID` — GUID of your custom build-tool installer task

Or one-shot flags: `--url` `--org` `--pat` `--task-id`

## How it works

1. Basic auth with PAT (`:` + token)  
2. List projects → list build definitions  
3. GET definition JSON (`api-version=6.0`)  
4. Find build-tool steps by task GUID, or change `queueStatus` / TFVC mapping  
5. PUT or DELETE

## Project layout

```text
portfolio-azure-pipeline-admin/
├── README.md
├── INTERVIEW.md
└── src/
    ├── NuGet.Config
    ├── AzurePipelineAdmin.sln
    └── AzurePipelineAdmin/
        ├── Program.cs
        ├── Commands/
        ├── Models/
        └── Services/
```

## Stack

- C# / .NET 8
- Azure DevOps REST API
- Newtonsoft.Json

## Disclaimer

Sanitized portfolio sample — no real org names, repo paths, or access tokens.
