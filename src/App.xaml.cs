using OneMusic.Core;
using OneMusic.UI;

namespace OneMusic;

public partial class App : Application
{
    public static MainWindow? MainWin { get; private set; }

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            Log.Error("UnhandledException", e.Exception);
            e.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("UnobservedTaskException", e.Exception);
            e.SetObserved();
        };
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        Log.Info("앱 시작");
        await Auth.Load();
        MainWin = new MainWindow();
        MainWin.Activate();
    }

    public static void SaveSession() => _ = Auth.Save();
}
