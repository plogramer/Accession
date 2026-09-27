# Accession

*Inventory, hash, and report every media you receive.*

Accession is a Windows desktop application for eDiscovery teams. It scans media folders on a
network share, records every folder and file (with metadata and SHA-1 hash) into a single SQLite
inventory file, shows dashboards by media, category and extension, and exports the inventory to Excel.

Requirements: [`requirements/001-initial_requirements.md`](requirements/001-initial_requirements.md)

## Solution layout

| Project | Purpose |
|---|---|
| `src/Accession.App` | WPF user interface (`net10.0-windows`, x64) |
| `src/Accession.Core` | Services, scanning, models – no UI references |
| `src/Accession.Data` | SQLite data access |
| `tests/Accession.Tests` | xUnit v3 unit and integration tests |

## Prerequisites

- Windows 10/11 x64
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Visual Studio 2026 (or 2022 17.14+) with the *.NET desktop development* workload, or VS Code / Rider

## Build, test, run

```powershell
dotnet build Accession.sln -c Release
dotnet test --solution Accession.sln -c Release
dotnet run --project src/Accession.App
```

Tests use [Microsoft.Testing.Platform](https://aka.ms/dotnet-test-mtp) (opted in via `global.json`).

## Publish (self-contained, win-x64)

```powershell
dotnet publish src/Accession.App -p:PublishProfile=win-x64
```

Output: `artifacts/publish/win-x64/`.

## Runtime locations

| What | Where |
|---|---|
| User settings | `%APPDATA%\Accession\settings.json` |
| Application log | `%LOCALAPPDATA%\Accession\logs\` |
