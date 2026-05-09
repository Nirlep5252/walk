using System.Windows;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Walk.ViewModels;

namespace Walk;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly MainViewModel _viewModel;
    private Storyboard? _currentStoryboard;
    private Storyboard? _viewSwitchStoryboard;
    private int _showAnimationVersion;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = _viewModel;
        InitializeComponent();

        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.ResultsViewMode))
                Dispatcher.BeginInvoke(AnimateActiveResultsView);
        };

        Deactivated += (_, _) => _viewModel.Hide();
    }

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.G)
        {
            _viewModel.ToggleResultsViewCommand.Execute(null);
            e.Handled = true;
            return;
        }

        var gesture = GetGestureText(e);
        if (gesture is not null && _viewModel.TryExecuteSelectedAction(gesture))
        {
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Left when _viewModel.IsGridView:
                _viewModel.MoveSelection(-1);
                e.Handled = true;
                break;

            case Key.Right when _viewModel.IsGridView:
                _viewModel.MoveSelection(1);
                e.Handled = true;
                break;

            case Key.Escape:
                _viewModel.Hide();
                e.Handled = true;
                break;

            case Key.Down:
                _viewModel.MoveSelection(_viewModel.IsGridView ? GetGridColumnCount() : 1);
                e.Handled = true;
                break;

            case Key.Up:
                _viewModel.MoveSelection(_viewModel.IsGridView ? -GetGridColumnCount() : -1);
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

        if (modifiers == ModifierKeys.Control && key is Key.Up or Key.Down)
            return $"Ctrl+{key}";

        return null;
    }

    private int GetGridColumnCount()
    {
        return 3;
    }

    private void GridCard_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: GridResultItemViewModel item })
            _ = item.Result.EnsurePreviewAsync();
    }

    public void ShowLauncher()
    {
        _viewModel.Show();

        // Position in upper third of primary screen
        var screen = SystemParameters.WorkArea;
        var targetTop = screen.Height * 0.2 + screen.Top;
        Left = (screen.Width - Width) / 2 + screen.Left;
        Top = targetTop - 18;

        // Cancel any running animation
        _currentStoryboard?.Stop(this);
        var showAnimationVersion = ++_showAnimationVersion;

        // Set initial state
        Opacity = 0.88;
        RootGrid.Opacity = 1;
        RootScaleTransform.ScaleX = 1;
        RootScaleTransform.ScaleY = 1;
        RootTranslateTransform.Y = 0;

        Show();
        Activate();
        SearchBox.Focus();

        Dispatcher.BeginInvoke(
            () => StartShowAnimation(showAnimationVersion, targetTop),
            DispatcherPriority.Render);
    }

    private void StartShowAnimation(int showAnimationVersion, double targetTop)
    {
        if (showAnimationVersion != _showAnimationVersion || !_viewModel.IsVisible || !IsVisible)
            return;

        // Animate in
        var movementDuration = TimeSpan.FromMilliseconds(240);
        var opacityDuration = TimeSpan.FromMilliseconds(150);
        var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };

        var opacityAnim = new DoubleAnimation(Opacity, 1, new Duration(opacityDuration)) { EasingFunction = ease };
        var topAnim = new DoubleAnimation(Top, targetTop, new Duration(movementDuration)) { EasingFunction = ease };

        var storyboard = new Storyboard();
        storyboard.Children.Add(opacityAnim);
        storyboard.Children.Add(topAnim);

        Storyboard.SetTarget(opacityAnim, this);
        Storyboard.SetTargetProperty(opacityAnim, new PropertyPath(OpacityProperty));

        Storyboard.SetTarget(topAnim, this);
        Storyboard.SetTargetProperty(topAnim, new PropertyPath(TopProperty));

        _currentStoryboard = storyboard;
        storyboard.Completed += (_, _) =>
        {
            Opacity = 1;
            Top = targetTop;
            RootScaleTransform.ScaleX = 1;
            RootScaleTransform.ScaleY = 1;
            RootTranslateTransform.Y = 0;
        };
        storyboard.Begin(this);
    }

    public void HideLauncher()
    {
        // Cancel any running animation
        _currentStoryboard?.Stop(this);
        ++_showAnimationVersion;

        var duration = TimeSpan.FromMilliseconds(100);
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

        var opacityAnim = new DoubleAnimation(Opacity, 0, new Duration(duration)) { EasingFunction = ease };
        var topAnim = new DoubleAnimation(Top, Top - 8, new Duration(duration)) { EasingFunction = ease };

        var storyboard = new Storyboard();
        storyboard.Children.Add(opacityAnim);
        storyboard.Children.Add(topAnim);

        Storyboard.SetTarget(opacityAnim, this);
        Storyboard.SetTargetProperty(opacityAnim, new PropertyPath(OpacityProperty));

        Storyboard.SetTarget(topAnim, this);
        Storyboard.SetTargetProperty(topAnim, new PropertyPath(TopProperty));

        storyboard.Completed += (_, _) =>
        {
            Hide();
            Opacity = 1;
            RootGrid.Opacity = 1;
            RootScaleTransform.ScaleX = 1;
            RootScaleTransform.ScaleY = 1;
            RootTranslateTransform.Y = 0;
        };

        _currentStoryboard = storyboard;
        storyboard.Begin(this);
    }

    private void AnimateActiveResultsView()
    {
        _viewSwitchStoryboard?.Stop(this);

        var target = _viewModel.IsGridView ? ResultsGrid : ResultsList;
        var translate = _viewModel.IsGridView ? ResultsGridTranslate : ResultsListTranslate;
        var startX = _viewModel.IsGridView ? 12 : -12;
        var duration = TimeSpan.FromMilliseconds(120);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        translate.X = startX;
        target.Opacity = 0.94;

        var translateAnim = new DoubleAnimation(startX, 0, new Duration(duration)) { EasingFunction = ease };
        var opacityAnim = new DoubleAnimation(0.94, 1, new Duration(duration)) { EasingFunction = ease };

        var storyboard = new Storyboard();
        storyboard.Children.Add(translateAnim);
        storyboard.Children.Add(opacityAnim);

        Storyboard.SetTarget(translateAnim, translate);
        Storyboard.SetTargetProperty(translateAnim, new PropertyPath(TranslateTransform.XProperty));

        Storyboard.SetTarget(opacityAnim, target);
        Storyboard.SetTargetProperty(opacityAnim, new PropertyPath(OpacityProperty));

        _viewSwitchStoryboard = storyboard;
        storyboard.Begin(this);
    }
}
