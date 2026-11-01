# Build CopyPath Comparer

Compares **actual file locations in a .NET build output folder** with **declared `CopyPath` values in a product-model XML**, then writes Excel reports of the differences.

## Problem

In large product builds, packaging metadata (product model) says where each assembly/file should be copied. The real build tree can drift. Manual checking is slow and error-prone.

## What it does

1. Scans the build app root (skips known non-package folders)
2. Parses product-model XML (`Assembly` + `OutputFile` nodes)
3. Normalizes multi-path `CopyPath` values
4. Diffs by file name:
   - path mismatch
   - only in build output
   - only in product model
5. Exports three Excel files:
   - build-output inventory
   - product-model inventory
   - difference report (with partition / component info when available)

```text
Build output tree          Product model XML
(relative folders)   vs    (CopyPath attributes)
           \               /
            \             /
             ▼           ▼
           Excel diff report
```

## Run

```bash
cd src/BuildCopyPathComparer
dotnet run -- "D:\Lab\Builds\App-net8.0" --out "D:\Lab\Reports"
```

| Argument | Meaning |
|---|---|
| `buildAppRoot` | Build output app folder (e.g. `App-net8.0`) |
| `--model` | Optional product-model XML path (default: `ComponentModel\ProductModel.xml`) |
| `--out` | Folder for Excel reports (default: current directory) |

## Stack

- C# / .NET 8
- LINQ + `System.Xml.Linq`
- ClosedXML

## Notes

- Generic sample names only (no company / product prefixes).
- Skips noise such as `.pdb`, `resources.dll`, `.deps.json` from the diff set.
