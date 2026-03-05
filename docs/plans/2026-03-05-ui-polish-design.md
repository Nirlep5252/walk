# UI/UX Polish Design — Raycast-style Animations & Emoji Branding

**Date:** 2026-03-05
**Status:** Approved

## Goal

Transform Walk from functional to premium by adding Raycast-style animations, smooth transitions, and emoji-based branding. Focus on motion and polish — the layout and color palette stay.

## 1. Window Open/Close Animation

**Show (ShowLauncher):**
- Start: `Opacity=0`, `ScaleTransform(0.97, 0.97)` centered
- End: `Opacity=1`, `Scale(1.0, 1.0)`
- Duration: **150ms**, `QuadraticEaseOut`
- SearchBox focused immediately (not waiting for animation completion)

**Hide (HideLauncher):**
- Reverse: `Opacity→0`, `Scale→0.97`
- Duration: **100ms**, `QuadraticEaseIn`
- `Window.Hide()` called in `Completed` event
- Cancel any in-progress animation before starting

**Location:** `MainWindow.xaml.cs` code-behind. Add `RenderTransformOrigin="0.5,0.5"` + `ScaleTransform` to root Grid in XAML.

## 2. Hover & Selection Transitions

Replace instant `ControlTemplate.Triggers` with `Storyboard`-based animations:

**Hover:**
- Background/border `ColorAnimation` over **100ms**
- Subtle `ScaleTransform(1.01, 1.01)` on the item border, centered

**Selection:**
- Smooth color transition over **120ms**
- Brighter border glow

**Implementation:** Named brushes in the ControlTemplate, animate `SolidColorBrush.Color`. `ScaleTransform` on `ItemChrome` Border via `RenderTransform`.

## 3. Emoji Tray Icon & Logo

**New class: `Helpers/EmojiIconGenerator.cs`**
- Renders emoji to `System.Drawing.Bitmap` via GDI+ `Graphics.DrawString`
- Converts to `System.Drawing.Icon` via `Icon.FromHandle(bitmap.GetHicon())`
- Font: "Segoe UI Emoji", size calculated to fit 32x32

**Tray states:**
- Default: 🚶 (walking person, U+1F6B6)
- Active: 🏃 (running person, U+1F3C3)

**Header logo:**
- Replace `TextBlock "W"` with emoji `TextBlock` using `FontFamily="Segoe UI Emoji"`, `FontSize="20"`
- Remove or simplify the background box

**App.xaml.cs:**
- Replace `LoadIconResource` with `EmojiIconGenerator.Create(emoji, size)`
- Keep `.ico` files as fallback

## 4. Empty State Polish

**Breathing hint text:**
- Opacity oscillates `0.6 → 1.0` over **2 seconds**
- `DoubleAnimation` with `AutoReverse` and `RepeatBehavior="Forever"`

**Search icon:**
- Add 🔍 emoji above "Start typing to launch something"
- 32px, Segoe UI Emoji font
- Same subtle opacity pulse

**Implementation:** Pure XAML `EventTrigger` on `Loaded`.

## Files Touched

| File | Changes |
|------|---------|
| `MainWindow.xaml` | RenderTransform on root Grid, replace Triggers with Storyboard animations, empty state icon+animation, logo emoji |
| `MainWindow.xaml.cs` | ShowLauncher/HideLauncher animation logic |
| `Helpers/EmojiIconGenerator.cs` | New file — emoji-to-icon rendering |
| `App.xaml.cs` | Replace LoadIconResource with EmojiIconGenerator |

## Non-Goals

- No layout changes (widths, paddings, grid structure stay)
- No color palette changes
- No new NuGet packages
- No font changes
