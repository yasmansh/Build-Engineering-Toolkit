# Assembly Dependent Finder

Interactive/console tool that answers: **who depends on this assembly?**

It checks:

1. Build-output DLLs whose assembly references include the target  
2. Source `.csproj` files that mention the target DLL/name  
3. Optional product-model metadata (`CopyPath`, build project path)

## When it helps

A shared/third-party DLL changed between two builds. Before updating packaging, you need the blast radius: which built modules and which source projects pull it in.

## Run

```bash
cd src/AssemblyDependentFinder
dotnet run -- "D:\Lab\Builds\App-net8.0" "D:\Lab\Builds\App-net8.0\ComponentModel\ProductModel.xml" --src "D:\Src\App\Main" --assembly Newtonsoft.Json
```

Without `--assembly`, the tool enters an interactive loop (type an assembly name, or `exit`).

| Argument | Meaning |
|---|---|
| `buildAppRoot` | Build output app folder |
| `productModelXml` | Product-model XML with Assembly nodes |
| `--src` | Source root to scan for `.csproj` (repeatable) |
| `--assembly` | One-shot assembly name (optional) |
| `--map-from` / `--map-to` | Rewrite BuildProject path prefix (default `$` → `D:\Src`) |

## Stack

- C# / .NET 8
- `System.Reflection` (`Assembly.LoadFrom`, `GetReferencedAssemblies`)
- `System.Xml.Linq`

## Notes

- Generic paths/names only.
- Skips `*.resources.dll`.
- Unloadable native binaries are ignored safely.
