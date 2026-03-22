# Walk

Walk is a quick launcher for Windows.

It sits in the system tray and opens a small search box so you can do common actions from one place:

- launch apps
- calculate expressions like `2+2`
- convert currencies like `100 USD to EUR`
- run system commands like `lock`
- launch Windows Run targets like `services.msc`, `shell:startup`, or `ms-settings:display`
- search files instantly with bundled indexed search, including queries like `*.pdf`
- browse folders and paths like `C:\Windows`

## What It Feels Like

Press the global hotkey, type what you want, and hit Enter.

Walk is built for fast keyboard use with a simple floating UI, result list, and tray-based background app behavior.

## Current Status

This is an early Windows-only project built with .NET and WPF.

Current features include:

- app search from Start Menu shortcuts and executables on `PATH`
- calculator results inline in the launcher
- cached currency conversion
- Windows Run commands, settings URIs, and shell folders
- recent Run command recall
- bundled fast file search with wildcard and filename queries
- direct path browsing for folders and partial paths
- tray icon and auto-start support
- installed-build auto-update support

File search is self-contained. Walk now ships a bundled `Everything` runtime for fast indexed filename search, so end users do not need to install any separate search tool.

## Run It

```powershell
dotnet run --project .\src\Walk\Walk.csproj
```

## Test It

```powershell
dotnet test
```
