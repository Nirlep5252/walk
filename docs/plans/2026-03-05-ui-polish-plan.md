# UI/UX Polish Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add Raycast-style animations, smooth transitions, and emoji-based branding to Walk.

**Architecture:** Pure WPF animations via Storyboards (XAML) and code-behind. One new helper class (EmojiIconGenerator) for rendering emoji to tray icons. No new NuGet packages.

**Tech Stack:** C# / WPF / .NET 8 / Wpf.Ui 4.2.0, GDI+ for emoji-to-icon rendering

---

### Task 1: EmojiIconGenerator — Failing Test

**Files:**
- Create: `tests/Walk.Tests/Helpers/EmojiIconGeneratorTests.cs`

**Step 1: Write the failing test**

```csharp
using System.Drawing;
using FluentAssertions;
using Walk.Helpers;

namespace Walk.Tests.Helpers;

public class EmojiIconGeneratorTests
{
    [Fact]
    public void Create_ReturnsIconWithRequestedSize()
    {
        var icon = EmojiIconGenerator.Create("🚶", 32);

        icon.Should().NotBeNull();
        icon.Width.Should().Be(32);
        icon.Height.Should().Be(32);
    }

    [Fact]
    public void Create_WithDifferentEmoji_ReturnsIcon()
    {
        var icon = EmojiIconGenerator.Create("🏃", 32);

        icon.Should().NotBeNull();
        icon.Width.Should().Be(32);
        icon.Height.Should().Be(32);
    }

    [Fact]
    public void Create_ProducesNonEmptyBitmap()
    {
        var icon = EmojiIconGenerator.Create("🚶", 32);

        using var bmp = icon.ToBitmap();
        // At least some pixels should be non-transparent (emoji was rendered)
        var hasContent = false;
        for (var x = 0; x < bmp.Width && !hasContent; x++)
            for (var y = 0; y < bmp.Height && !hasContent; y++)
                if (bmp.GetPixel(x, y).A > 0)
                    hasContent = true;

        hasContent.Should().BeTrue("emoji should render visible pixels");
    }

    [Fact]
    public void Create_16x16_ReturnsSmallIcon()
    {
        var icon = EmojiIconGenerator.Create("🔍", 16);

        icon.Should().NotBeNull();
        icon.Width.Should().Be(16);
        icon.Height.Should().Be(16);
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/Walk.Tests --filter "FullyQualifiedName~EmojiIconGeneratorTests" --no-restore -v minimal`
Expected: FAIL — `EmojiIconGenerator` class does not exist

**Step 3: Commit**

```bash
git add tests/Walk.Tests/Helpers/EmojiIconGeneratorTests.cs
git commit -m "test: add failing tests for EmojiIconGenerator"
```

---

### Task 2: EmojiIconGenerator — Implementation

**Files:**
- Create: `src/Walk/Helpers/EmojiIconGenerator.cs`

**Step 1: Implement EmojiIconGenerator**

```csharp
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Walk.Helpers;

public static class EmojiIconGenerator
{
    public static Icon Create(string emoji, int size)
    {
        using var bitmap = new Bitmap(size, size);
        bitmap.SetResolution(96, 96);

        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.Clear(Color.Transparent);

        // Use Segoe UI Emoji which is always available on Windows 10/11
        var fontSize = size * 0.7f;
        using var font = new Font("Segoe UI Emoji", fontSize, FontStyle.Regular, GraphicsUnit.Pixel);

        // Measure and center the emoji
        var textSize = graphics.MeasureString(emoji, font);
        var x = (size - textSize.Width) / 2f;
        var y = (size - textSize.Height) / 2f;

        graphics.DrawString(emoji, font, Brushes.White, x, y);

        var hIcon = bitmap.GetHicon();
        return Icon.FromHandle(hIcon);
    }
}
```

**Step 2: Run tests to verify they pass**

Run: `dotnet test tests/Walk.Tests --filter "FullyQualifiedName~EmojiIconGeneratorTests" --no-restore -v minimal`
Expected: All 4 tests PASS

**Step 3: Commit**

```bash
git add src/Walk/Helpers/EmojiIconGenerator.cs
git commit -m "feat: add EmojiIconGenerator for rendering emoji to tray icons"
```

---

### Task 3: Wire Emoji Tray Icons into App.xaml.cs

**Files:**
- Modify: `src/Walk/App.xaml.cs` (lines 96-140)

**Step 1: Replace icon loading in SetupTray**

In `App.xaml.cs`, replace the icon loading lines (105-108):

```csharp
// OLD:
_trayDefaultIcon = LoadIconResource("Assets/walk-tray.ico") ??
    (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
_trayActiveIcon = LoadIconResource("Assets/walk-tray-active.ico") ??
    (System.Drawing.Icon)_trayDefaultIcon.Clone();

// NEW:
_trayDefaultIcon = EmojiIconGenerator.Create("🚶", 32);
_trayActiveIcon = EmojiIconGenerator.Create("🏃", 32);
```

Add the using at the top of the file:
```csharp
using Walk.Helpers;
```

**Step 2: Build to verify**

Run: `dotnet build src/Walk/Walk.csproj --no-restore -v minimal`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add src/Walk/App.xaml.cs
git commit -m "feat: use emoji tray icons (walking/running person)"
```

---

### Task 4: Window Open/Close Animation — XAML Setup

**Files:**
- Modify: `src/Walk/MainWindow.xaml` (lines 84-85, the root Grid)

**Step 1: Add RenderTransform to root Grid**

Replace line 84:
```xml
<Grid Margin="14">
```

With:
```xml
<Grid x:Name="RootGrid" Margin="14" RenderTransformOrigin="0.5,0.5">
    <Grid.RenderTransform>
        <ScaleTransform x:Name="RootScaleTransform" ScaleX="1" ScaleY="1" />
    </Grid.RenderTransform>
```

**Step 2: Build to verify XAML compiles**

Run: `dotnet build src/Walk/Walk.csproj --no-restore -v minimal`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add src/Walk/MainWindow.xaml
git commit -m "feat: add ScaleTransform to root grid for window animations"
```

---

### Task 5: Window Open/Close Animation — Code-Behind

**Files:**
- Modify: `src/Walk/MainWindow.xaml.cs`

**Step 1: Replace ShowLauncher and HideLauncher with animated versions**

Replace the entire `MainWindow.xaml.cs` with:

```csharp
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Walk.ViewModels;

namespace Walk;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly MainViewModel _viewModel;
    private Storyboard? _currentStoryboard;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = _viewModel;
        InitializeComponent();

        Deactivated += (_, _) => _viewModel.Hide();
    }

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                _viewModel.Hide();
                e.Handled = true;
                break;

            case Key.Enter when Keyboard.Modifiers == ModifierKeys.Control:
                _viewModel.ExecuteAsAdminCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Enter:
                _viewModel.ExecuteSelectedCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Down:
                if (_viewModel.SelectedIndex < _viewModel.Results.Count - 1)
                    _viewModel.SelectedIndex++;
                e.Handled = true;
                break;

            case Key.Up:
                if (_viewModel.SelectedIndex > 0)
                    _viewModel.SelectedIndex--;
                e.Handled = true;
                break;
        }

        base.OnPreviewKeyDown(e);
    }

    public void ShowLauncher()
    {
        _viewModel.Show();

        // Position in upper third of primary screen
        var screen = SystemParameters.WorkArea;
        Left = (screen.Width - Width) / 2 + screen.Left;
        Top = screen.Height * 0.2 + screen.Top;

        // Cancel any running animation
        _currentStoryboard?.Stop(this);

        // Set initial state
        Opacity = 0;
        RootScaleTransform.ScaleX = 0.97;
        RootScaleTransform.ScaleY = 0.97;

        Show();
        Activate();
        SearchBox.Focus();

        // Animate in
        var duration = TimeSpan.FromMilliseconds(150);
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };

        var opacityAnim = new DoubleAnimation(0, 1, new Duration(duration)) { EasingFunction = ease };
        var scaleXAnim = new DoubleAnimation(0.97, 1.0, new Duration(duration)) { EasingFunction = ease };
        var scaleYAnim = new DoubleAnimation(0.97, 1.0, new Duration(duration)) { EasingFunction = ease };

        var storyboard = new Storyboard();
        storyboard.Children.Add(opacityAnim);
        storyboard.Children.Add(scaleXAnim);
        storyboard.Children.Add(scaleYAnim);

        Storyboard.SetTarget(opacityAnim, this);
        Storyboard.SetTargetProperty(opacityAnim, new PropertyPath(OpacityProperty));

        Storyboard.SetTarget(scaleXAnim, RootScaleTransform);
        Storyboard.SetTargetProperty(scaleXAnim, new PropertyPath(ScaleTransform.ScaleXProperty));

        Storyboard.SetTarget(scaleYAnim, RootScaleTransform);
        Storyboard.SetTargetProperty(scaleYAnim, new PropertyPath(ScaleTransform.ScaleYProperty));

        _currentStoryboard = storyboard;
        storyboard.Begin(this);
    }

    public void HideLauncher()
    {
        // Cancel any running animation
        _currentStoryboard?.Stop(this);

        var duration = TimeSpan.FromMilliseconds(100);
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseIn };

        var opacityAnim = new DoubleAnimation(Opacity, 0, new Duration(duration)) { EasingFunction = ease };
        var scaleXAnim = new DoubleAnimation(RootScaleTransform.ScaleX, 0.97, new Duration(duration)) { EasingFunction = ease };
        var scaleYAnim = new DoubleAnimation(RootScaleTransform.ScaleY, 0.97, new Duration(duration)) { EasingFunction = ease };

        var storyboard = new Storyboard();
        storyboard.Children.Add(opacityAnim);
        storyboard.Children.Add(scaleXAnim);
        storyboard.Children.Add(scaleYAnim);

        Storyboard.SetTarget(opacityAnim, this);
        Storyboard.SetTargetProperty(opacityAnim, new PropertyPath(OpacityProperty));

        Storyboard.SetTarget(scaleXAnim, RootScaleTransform);
        Storyboard.SetTargetProperty(scaleXAnim, new PropertyPath(ScaleTransform.ScaleXProperty));

        Storyboard.SetTarget(scaleYAnim, RootScaleTransform);
        Storyboard.SetTargetProperty(scaleYAnim, new PropertyPath(ScaleTransform.ScaleYProperty));

        storyboard.Completed += (_, _) =>
        {
            Hide();
            Opacity = 1;
            RootScaleTransform.ScaleX = 1;
            RootScaleTransform.ScaleY = 1;
        };

        _currentStoryboard = storyboard;
        storyboard.Begin(this);
    }
}
```

**Step 2: Build to verify**

Run: `dotnet build src/Walk/Walk.csproj --no-restore -v minimal`
Expected: Build succeeded

**Step 3: Run all tests to verify no regressions**

Run: `dotnet test tests/Walk.Tests --no-restore -v minimal`
Expected: All tests pass

**Step 4: Commit**

```bash
git add src/Walk/MainWindow.xaml.cs
git commit -m "feat: add snappy fade+scale window open/close animations"
```

---

### Task 6: Hover & Selection Animated Transitions

**Files:**
- Modify: `src/Walk/MainWindow.xaml` (lines 46-81, the `ResultItemStyle`)

**Step 1: Replace the ResultItemStyle with animated version**

Replace the entire `<Style x:Key="ResultItemStyle" ...>` block (lines 46-81) with:

```xml
<Style x:Key="ResultItemStyle" TargetType="ListBoxItem">
    <Setter Property="HorizontalContentAlignment" Value="Stretch" />
    <Setter Property="Margin" Value="0,0,0,8" />
    <Setter Property="Padding" Value="0" />
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate TargetType="ListBoxItem">
                <Border
                    x:Name="ItemChrome"
                    CornerRadius="12"
                    Padding="12,10"
                    RenderTransformOrigin="0.5,0.5"
                    BorderThickness="1">
                    <Border.Background>
                        <SolidColorBrush x:Name="ItemBg" Color="#12000000" />
                    </Border.Background>
                    <Border.BorderBrush>
                        <SolidColorBrush x:Name="ItemBorder" Color="#2DFFFFFF" />
                    </Border.BorderBrush>
                    <Border.RenderTransform>
                        <ScaleTransform ScaleX="1" ScaleY="1" />
                    </Border.RenderTransform>
                    <ContentPresenter />
                </Border>
                <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                        <Trigger.EnterActions>
                            <BeginStoryboard>
                                <Storyboard>
                                    <ColorAnimation
                                        Storyboard.TargetName="ItemBg"
                                        Storyboard.TargetProperty="Color"
                                        To="#324D8FC9"
                                        Duration="0:0:0.10" />
                                    <ColorAnimation
                                        Storyboard.TargetName="ItemBorder"
                                        Storyboard.TargetProperty="Color"
                                        To="#64B3D8FF"
                                        Duration="0:0:0.10" />
                                    <DoubleAnimation
                                        Storyboard.TargetName="ItemChrome"
                                        Storyboard.TargetProperty="(UIElement.RenderTransform).(ScaleTransform.ScaleX)"
                                        To="1.01"
                                        Duration="0:0:0.10" />
                                    <DoubleAnimation
                                        Storyboard.TargetName="ItemChrome"
                                        Storyboard.TargetProperty="(UIElement.RenderTransform).(ScaleTransform.ScaleY)"
                                        To="1.01"
                                        Duration="0:0:0.10" />
                                </Storyboard>
                            </BeginStoryboard>
                        </Trigger.EnterActions>
                        <Trigger.ExitActions>
                            <BeginStoryboard>
                                <Storyboard>
                                    <ColorAnimation
                                        Storyboard.TargetName="ItemBg"
                                        Storyboard.TargetProperty="Color"
                                        To="#12000000"
                                        Duration="0:0:0.10" />
                                    <ColorAnimation
                                        Storyboard.TargetName="ItemBorder"
                                        Storyboard.TargetProperty="Color"
                                        To="#2DFFFFFF"
                                        Duration="0:0:0.10" />
                                    <DoubleAnimation
                                        Storyboard.TargetName="ItemChrome"
                                        Storyboard.TargetProperty="(UIElement.RenderTransform).(ScaleTransform.ScaleX)"
                                        To="1.0"
                                        Duration="0:0:0.10" />
                                    <DoubleAnimation
                                        Storyboard.TargetName="ItemChrome"
                                        Storyboard.TargetProperty="(UIElement.RenderTransform).(ScaleTransform.ScaleY)"
                                        To="1.0"
                                        Duration="0:0:0.10" />
                                </Storyboard>
                            </BeginStoryboard>
                        </Trigger.ExitActions>
                    </Trigger>
                    <Trigger Property="IsSelected" Value="True">
                        <Trigger.EnterActions>
                            <BeginStoryboard>
                                <Storyboard>
                                    <ColorAnimation
                                        Storyboard.TargetName="ItemBg"
                                        Storyboard.TargetProperty="Color"
                                        To="#5666A2DF"
                                        Duration="0:0:0.12" />
                                    <ColorAnimation
                                        Storyboard.TargetName="ItemBorder"
                                        Storyboard.TargetProperty="Color"
                                        To="#88CAE5FF"
                                        Duration="0:0:0.12" />
                                </Storyboard>
                            </BeginStoryboard>
                        </Trigger.EnterActions>
                        <Trigger.ExitActions>
                            <BeginStoryboard>
                                <Storyboard>
                                    <ColorAnimation
                                        Storyboard.TargetName="ItemBg"
                                        Storyboard.TargetProperty="Color"
                                        To="#12000000"
                                        Duration="0:0:0.12" />
                                    <ColorAnimation
                                        Storyboard.TargetName="ItemBorder"
                                        Storyboard.TargetProperty="Color"
                                        To="#2DFFFFFF"
                                        Duration="0:0:0.12" />
                                </Storyboard>
                            </BeginStoryboard>
                        </Trigger.ExitActions>
                    </Trigger>
                    <Trigger Property="IsEnabled" Value="False">
                        <Setter TargetName="ItemChrome" Property="Opacity" Value="0.6" />
                    </Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>
```

**Step 2: Build to verify XAML compiles**

Run: `dotnet build src/Walk/Walk.csproj --no-restore -v minimal`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add src/Walk/MainWindow.xaml
git commit -m "feat: add smooth animated hover/selection transitions on result items"
```

---

### Task 7: Logo — Replace "W" Text with Emoji

**Files:**
- Modify: `src/Walk/MainWindow.xaml` (lines 109-123, the logo Border)

**Step 1: Replace the logo block**

Replace the logo Border (lines 109-123):

```xml
<!-- OLD -->
<Border
    Width="30"
    Height="30"
    CornerRadius="8"
    Background="#5F4D8FC9"
    BorderBrush="#88B2D9FF"
    BorderThickness="1">
    <TextBlock
        Text="W"
        FontSize="16"
        FontWeight="Bold"
        Foreground="#F1FFFFFF"
        HorizontalAlignment="Center"
        VerticalAlignment="Center" />
</Border>
```

With:

```xml
<!-- NEW -->
<TextBlock
    Text="🚶"
    FontFamily="Segoe UI Emoji"
    FontSize="26"
    VerticalAlignment="Center" />
```

**Step 2: Build to verify**

Run: `dotnet build src/Walk/Walk.csproj --no-restore -v minimal`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add src/Walk/MainWindow.xaml
git commit -m "feat: replace W text logo with walking person emoji"
```

---

### Task 8: Empty State — Search Icon + Breathing Animation

**Files:**
- Modify: `src/Walk/MainWindow.xaml` (lines 280-296, the empty state StackPanel)

**Step 1: Replace empty state StackPanel**

Replace the empty state block (lines 280-296):

```xml
<!-- OLD -->
<StackPanel
    HorizontalAlignment="Center"
    VerticalAlignment="Center"
    Visibility="{Binding Results.Count, Converter={StaticResource ZeroCountToVisibilityConverter}}">
    <TextBlock
        Text="Start typing to launch something"
        FontSize="17"
        FontFamily="Segoe UI Variable Display Semibold"
        HorizontalAlignment="Center"
        Foreground="{DynamicResource TextFillColorPrimaryBrush}" />
    <TextBlock
        Margin="0,6,0,0"
        Text="Try: edge, calc 42*19, usd to eur, file readme"
        FontSize="12"
        HorizontalAlignment="Center"
        Foreground="{StaticResource HintTextBrush}" />
</StackPanel>
```

With:

```xml
<!-- NEW -->
<StackPanel
    HorizontalAlignment="Center"
    VerticalAlignment="Center"
    Visibility="{Binding Results.Count, Converter={StaticResource ZeroCountToVisibilityConverter}}">
    <TextBlock
        Text="🔍"
        FontFamily="Segoe UI Emoji"
        FontSize="32"
        HorizontalAlignment="Center"
        Margin="0,0,0,8">
        <TextBlock.Triggers>
            <EventTrigger RoutedEvent="Loaded">
                <BeginStoryboard>
                    <Storyboard>
                        <DoubleAnimation
                            Storyboard.TargetProperty="Opacity"
                            From="0.6"
                            To="1.0"
                            Duration="0:0:2"
                            AutoReverse="True"
                            RepeatBehavior="Forever" />
                    </Storyboard>
                </BeginStoryboard>
            </EventTrigger>
        </TextBlock.Triggers>
    </TextBlock>
    <TextBlock
        Text="Start typing to launch something"
        FontSize="17"
        FontFamily="Segoe UI Variable Display Semibold"
        HorizontalAlignment="Center"
        Foreground="{DynamicResource TextFillColorPrimaryBrush}" />
    <TextBlock
        Margin="0,6,0,0"
        FontSize="12"
        HorizontalAlignment="Center"
        Foreground="{StaticResource HintTextBrush}">
        <TextBlock.Text>Try: edge, calc 42*19, usd to eur, file readme</TextBlock.Text>
        <TextBlock.Triggers>
            <EventTrigger RoutedEvent="Loaded">
                <BeginStoryboard>
                    <Storyboard>
                        <DoubleAnimation
                            Storyboard.TargetProperty="Opacity"
                            From="0.6"
                            To="1.0"
                            Duration="0:0:2"
                            AutoReverse="True"
                            RepeatBehavior="Forever" />
                    </Storyboard>
                </BeginStoryboard>
            </EventTrigger>
        </TextBlock.Triggers>
    </TextBlock>
</StackPanel>
```

**Step 2: Build to verify**

Run: `dotnet build src/Walk/Walk.csproj --no-restore -v minimal`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add src/Walk/MainWindow.xaml
git commit -m "feat: add search emoji and breathing animation to empty state"
```

---

### Task 9: Final Build + Full Test Suite

**Step 1: Clean build**

Run: `dotnet build Walk.sln --no-restore -v minimal`
Expected: Build succeeded, 0 warnings relevant to our changes

**Step 2: Run full test suite**

Run: `dotnet test Walk.sln --no-restore -v minimal`
Expected: All tests pass (existing + 4 new EmojiIconGenerator tests)

**Step 3: Manual verification checklist**

Run the app and verify:
- [ ] Window fades+scales in on Ctrl+Alt+Space
- [ ] Window fades+scales out on Escape
- [ ] Result items smoothly transition colors on hover
- [ ] Result items subtly scale up on hover
- [ ] Selected item has smooth color transition
- [ ] Tray icon shows 🚶 when launcher is hidden
- [ ] Tray icon shows 🏃 when launcher is visible
- [ ] Logo in header shows 🚶 emoji
- [ ] Empty state shows 🔍 with breathing animation
- [ ] Hint text pulses subtly
- [ ] Rapid show/hide doesn't glitch (animations cancel cleanly)
