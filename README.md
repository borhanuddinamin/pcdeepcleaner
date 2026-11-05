# Windows Cleanup Manager

An offline Windows 11 tool that shows which applications own which data, and lets you review and quarantine
leftovers and caches. It protects anything it isn't sure about.

## Build and test

```powershell
.\build.ps1
```

## Portable build

```powershell
dotnet publish .\src\WCM.Desktop\WCM.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\artifacts\portable
```

Keep the `Rules` folder beside the program. Add an empty `portable.marker` file there to keep data beside the
program too, in a folder you can write to.

## How cleanup works

1. Run a full scan from the Dashboard. This changes no files.
2. In Settings, enable the cleanup rules you have reviewed. All start off.
3. Approve leftovers in Orphaned Data, or select items in Cache Analyzer.
4. In Cleanup Center, create a preview, approve it, then move items to quarantine.
5. In Quarantine & Restore, restore items or delete them permanently.

## Documents

Start with `docs/IMPLEMENTATION_STATUS.md` and `docs/IMPLEMENTATION_GAP_REPORT.md`.
