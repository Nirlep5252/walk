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
        var gesture = GetGestureText(e);
        if (gesture is not null && _viewModel.TryExecuteSelectedAction(gesture))
        {
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                _viewModel.Hide();
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

    private static string? GetGestureText(System.Windows.Input.KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt)
            return null;

        var modifiers = Keyboard.Modifiers;

        if (key == Key.Enter)
        {
            return modifiers switch
            {
                ModifierKeys.None => "Enter",
                ModifierKeys.Control => "Ctrl+Enter",
                _ => null,
            };
        }

        if (modifiers == ModifierKeys.Control && key is >= Key.A and <= Key.Z)
            return $"Ctrl+{key.ToString().ToUpperInvariant()}";

        return null;
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
