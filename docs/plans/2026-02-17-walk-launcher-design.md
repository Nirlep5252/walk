# Walk — Windows App Launcher Kit

## Design Document

**Date:** 2026-02-17
**Status:** Approved

---

## Summary

Walk is a Raycast-style application launcher for Windows 11. It provides a global hotkey-activated search bar that can launch apps, evaluate math expressions, convert currencies, execute system commands, and search files. Built with WPF + Wpf.Ui for native Win11 Fluent Design appearance.

## Tech Stack

- **Framework:** WPF on .NET 8
- **UI Library:** Wpf.Ui (Fluent Design controls for WPF)
- **Language:** C#
- **Currency API:** exchangerate-api.com (free tier, 1500 req/month)
- **Math Parser:** NCalc or custom expression evaluator

## Architecture

### Plugin-Based Query System

The core architecture is a plugin-based query router. The search bar is a single entry point. When the user types, a QueryRouter dispatches the input to all registered plugins in parallel. Each plugin evaluates whether it can handle the input and returns results.

```
User Input -> QueryRouter -> [All Plugins in parallel] -> Merged & Ranked Results -> ListView
```

### Smart Query Routing (No Prefixes)

All plugins evaluate every query simultaneously. The router merges results by confidence/priority:

| Input Pattern | Detection Heuristic | Primary Plugin |
|---|---|---|
| `notepad` | No math/currency/path pattern | App Search |
| `2+2`, `sin(45)` | Matches `^[\d\s+\-*/^().%]+$` or known functions | Calculator |
| `100 USD to EUR` | Matches `\d+\s*[A-Z]{3}\s*(to\|in)\s*[A-Z]{3}` | Currency |
| `shutdown`, `lock` | Fuzzy match against known system command list | System Commands |
| `C:\Users\`, `\Documents` | Starts with drive letter+colon or backslash | File Search |

All plugins always run. Results are ranked: most confident plugin's results appear first, app search results always included as fallback.

### Plugin Interface

```csharp
interface IQueryPlugin
{
    string Name { get; }
    int Priority { get; }
    bool IsMatch(string query);
    Task<List<SearchResult>> QueryAsync(string query, CancellationToken ct);
}
```

## Plugins

### App Search Plugin

- Indexes `.lnk` files from Start Menu directories and executables on PATH
- Directories: `%APPDATA%\Microsoft\Windows\Start Menu`, `%ProgramData%\Microsoft\Windows\Start Menu`
- Fuzzy substring + character-order matching
- Tracks usage frequency for ranking boost
- Actions: Run, Run as Admin, Open File Location

### Calculator Plugin

- Evaluates math expressions using NCalc or System.Linq.Expressions
- Supports: arithmetic, parentheses, trig functions, percentages, power, sqrt
- Result shown inline with Copy to Clipboard action
- Silent failure on invalid expressions (no results returned)

### Currency Plugin

- Parses natural patterns: `100 USD to EUR`, `50 eur in gbp`
- Fetches rates from exchangerate-api.com
- Cache: local JSON file, 6-hour TTL (configurable)
- Actions: Copy Result, Swap Currencies
- Graceful degradation: shows cached data on network failure

### System Commands Plugin

- Hardcoded command list: Shutdown, Restart, Sleep, Lock, Log Off, Empty Recycle Bin, Open Settings
- Fuzzy matched against query
- Confirmation prompt for destructive actions (shutdown, restart)

### File Search Plugin

- Triggered when input looks like a filesystem path
- Uses Directory.EnumerateFileSystemEntries for browsing
- Actions: Open, Open Containing Folder, Copy Path

## UI/UX Design

### Window

- Borderless floating window, ~650px wide
- Centered horizontally, positioned in upper third of screen
- Acrylic/Mica backdrop via Wpf.Ui
- Rounded corners (8px radius), drop shadow
- Fade-in animation (~150ms) on show
- Dismissed by Escape key or clicking outside

### Search Bar

- Large text input with search icon
- Placeholder: "Search apps, calculate, convert..."
- As-you-type results with ~100ms debounce

### Results List

- Below search bar, max ~8 visible items, scrollable
- Each row: icon + title + subtitle + keyboard shortcut hint
- Selected row: subtle accent background
- Grouped by plugin type with section dividers when multiple plugins match

### Interactions

- Enter: execute top result (or selected result)
- Arrow keys: navigate results
- Tab/Right-arrow on result: expand action menu
- Escape: dismiss window
- Alt+Space: global hotkey to show/hide

### Action Context Menu

- Apps: Run, Run as Admin, Open File Location
- Calculator: Copy Result
- Currency: Copy Result, Swap Currencies
- System Commands: Execute (with confirmation for destructive)
- Files: Open, Open Folder, Copy Path

## System Behavior

### Lifecycle

- Runs as system tray application
- Auto-starts with Windows (via registry or startup folder)
- Tray icon with context menu: Show Launcher, Settings, About, Quit
- Double-click tray icon shows launcher

### Global Hotkey

- Alt+Space via low-level keyboard hook (overrides default Win system menu)
- If registration fails, notify user to change hotkey in settings

### Theme

- Follows system theme (light/dark) automatically

## Data Storage

All data stored in `%APPDATA%\Walk\`:

### App Index (`appindex.json`)

- Built on first launch, rebuilt on app start (background)
- FileSystemWatcher on Start Menu directories for live updates
- Stores: app name, executable path, icon path, launch count, last used timestamp

### Currency Cache (`currency_cache.json`)

- Structure: `{ "base": "USD", "rates": { ... }, "fetched_at": "..." }`
- TTL: 6 hours (configurable)

### Settings (`settings.json`)

- Hotkey, theme (auto/light/dark), currency cache TTL, startup with Windows, max results

## Error Handling

- **Network failure (currency):** Show cached result with "(cached)" label; "No connection" if no cache
- **Invalid math expression:** Silent — no calculator results shown
- **App index corruption:** Delete and rebuild on next launch
- **Hotkey conflict:** Notification suggesting hotkey change in settings
- **API rate limit:** Fall back to cached currency data

## Cancellation

- Each query creates a CancellationTokenSource
- New keystroke cancels previous query token to prevent stale results

## Project Structure

```
Walk/
├── Walk.sln
├── Walk/
│   ├── App.xaml / App.xaml.cs
│   ├── MainWindow.xaml / .cs
│   ├── ViewModels/
│   │   ├── MainViewModel.cs
│   │   └── SettingsViewModel.cs
│   ├── Plugins/
│   │   ├── IQueryPlugin.cs
│   │   ├── AppSearchPlugin.cs
│   │   ├── CalculatorPlugin.cs
│   │   ├── CurrencyPlugin.cs
│   │   ├── SystemCommandPlugin.cs
│   │   └── FileSearchPlugin.cs
│   ├── Models/
│   │   ├── SearchResult.cs
│   │   ├── SearchAction.cs
│   │   └── AppEntry.cs
│   ├── Services/
│   │   ├── HotkeyService.cs
│   │   ├── TrayService.cs
│   │   ├── CacheService.cs
│   │   ├── AppIndexService.cs
│   │   ├── SettingsService.cs
│   │   └── QueryRouter.cs
│   ├── Helpers/
│   │   ├── FuzzyMatcher.cs
│   │   ├── ProcessHelper.cs
│   │   └── IconExtractor.cs
│   └── Assets/
├── Walk.Tests/
└── docs/plans/
```
