# Accession

*Inventory, hash, and report every media you receive.*

Accession is a Windows desktop application for eDiscovery teams. It scans media folders on a
network share, records every folder and file (with metadata and SHA-1 hash) into a single SQLite
inventory file, shows dashboards by media, category and extension, and exports the inventory to Excel.

Requirements: [`requirements/001-initial_requirements.md`](requirements/001-initial_requirements.md)

## Solution layout

| Project | Purpose |
|---|---|
| `src/Accession.App` | WPF host and classic screens (`net10.0-windows`, x64); hosts the web UI in a BlazorWebView |
| `src/Accession.UI` | Web UI (Blazor components and CSS), no WPF references – see `requirements/002-web-ui-plan.md` |
| `src/Accession.Core` | Services, scanning, models – no UI references |
| `src/Accession.Data` | SQLite data access |
| `tests/Accession.Tests` | xUnit v3 unit and integration tests |

## Prerequisites

- Windows 10/11 x64 with the [WebView2 runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (included in Windows 11)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Visual Studio 2026 with the *.NET desktop development* workload (Visual Studio 2022 does not support .NET 10), or Rider / VS Code with the C# Dev Kit
- `Accession.App` is the startup project (first in the solution); if Visual Studio shows another one in bold, right-click `Accession.App` → **Set as Startup Project**

## Build, test, run

```powershell
dotnet build Accession.sln -c Release
dotnet test --solution Accession.sln -c Release
dotnet run --project src/Accession.App
```

To try the new web-style screens (preview), open an inventory and choose **View → New UI (preview)**.

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
| WebView2 profile (web UI) | `%LOCALAPPDATA%\Accession\WebView2\` |
