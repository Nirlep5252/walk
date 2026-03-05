using System.Windows;
using System.Windows.Input;
using Walk.ViewModels;

namespace Walk;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly MainViewModel _viewModel;

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

        Show();
        Activate();
        SearchBox.Focus();
    }

    public void HideLauncher()
    {
        Hide();
    }
}
