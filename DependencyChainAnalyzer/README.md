# Assembly Dependency Chain Finder

Console tool that scans a .NET **build output folder**, builds an assembly **reference graph**, and prints dependency chains such as:

```text
A.dll
  -> B.dll
    -> C.dll
```

## What it does

1. Finds managed DLLs under a root path (optional include prefix filter)
2. Skips satellite `*.resources.dll` and optional exclude-prefix assemblies
3. Maps each file to its assembly name
4. Loads each assembly and reads `GetReferencedAssemblies()`
5. Keeps only references that also exist in the scanned set
6. Walks the graph and prints chains of a given depth (default: 3 → A → B → C)

```text
App-net8.0/**/Company.*.dll
        │
        ▼
  reference graph
        │
        ▼
   A -> B -> C chains  (console)
```

## Why it is useful

When a product build drops many first-party assemblies, it helps answer:

- Which modules depend on which other modules?
- Are there longer dependency chains that may affect build order or packaging?

This is a **static reference scan** of built DLLs (not a full NuGet restore graph).

## Requirements

- .NET 8 SDK
- A local build output folder containing managed assemblies

## Run

```bash
cd src/AssemblyDependencyChain
dotnet run -- "D:\Lab\Builds\App-net8.0" --include Company. --exclude Company.Shared. --depth 3
```

| Argument | Meaning |
|---|---|
| `rootPath` | Build output root to scan |
| `--include` | File name prefix filter (default in sample: `Company.`) |
| `--exclude` | Prefix to skip (default in sample: `Company.Shared.`) |
| `--depth` | Chain length to report (default: `3`) |

With no arguments, lab default paths/prefixes from `Program.cs` are used.

## Sample output

```text
Company.Orders.dll
  -> Company.Billing.dll
    -> Company.Core.dll

Assemblies scanned: 42
Chains found (depth 3): 17
```

## Notes / limits

- Uses `Assembly.LoadFrom` — some native or malformed binaries are skipped safely.
- Only references **inside the filtered set** are considered (not the whole GAC / framework graph).
- Cycle prevention: a name is not repeated inside one printed chain.
- This portfolio sample uses **generic names only** (no real product prefixes or internal paths).

## Project layout

```text
portfolio-dependency-chain/
├── README.md
├── INTERVIEW.md
└── src/
    ├── AssemblyDependencyChain.sln
    └── AssemblyDependencyChain/
        ├── AssemblyDependencyChain.csproj
        └── Program.cs
```

## Stack

- C# / .NET 8
- `System.Reflection` (`AssemblyName`, `Assembly.LoadFrom`, `GetReferencedAssemblies`)
