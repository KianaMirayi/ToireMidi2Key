using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ToireMidi2Key.App.ViewModels;
using ToireMidi2Key.App.Views;

namespace ToireMidi2Key.App;

public partial class App : Application
{
    private MainViewModel? _viewModel;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _viewModel = new MainViewModel();
            desktop.MainWindow = new MainWindow { DataContext = _viewModel };

            // 退出时必须停引擎并把所有按下的键松开，否则系统里会留下"卡住的键"
            desktop.ShutdownRequested += (_, _) => _viewModel.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
