# Walk

Walk is a quick launcher for Windows.

It sits in the system tray and opens a small search box so you can do common actions from one place:

- launch apps
- calculate expressions like `2+2`
- convert currencies like `100 USD to EUR`
- run system commands like `lock`
- browse files by typing a path like `C:\Windows`

## What It Feels Like

Press the global hotkey, type what you want, and hit Enter.

Walk is built for fast keyboard use with a simple floating UI, result list, and tray-based background app behavior.

## Current Status

This is an early Windows-only project built with .NET and WPF.

Current features include:

- app search from Start Menu shortcuts and executables on `PATH`
- calculator results inline in the launcher
- cached currency conversion
- file path browsing
- tray icon and auto-start support
- installed-build auto-update support

## Run It

```powershell
dotnet run --project .\src\Walk\Walk.csproj
```

## Test It

```powershell
dotnet test
```
