using Microsoft.UI.Xaml;

namespace QuranReader.App;

public partial class App : Application
{
    private Window? window;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        window = new MainWindow();
        window.Activate();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        try
        {
            var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuranReader", "logs");
            Directory.CreateDirectory(logDir);
            var logFile = Path.Combine(logDir, "crash.log");
            var details = $"[{DateTime.UtcNow:O}] UnhandledException: {e.Message}\n{e.Exception}\nStackTrace:\n{e.Exception?.StackTrace}\n---\n";
            File.AppendAllText(logFile, details);
        }
        catch { }

        // Log unexpected failures without concealing them. Expected detached-tree
        // races are guarded at their specific layout operation, not globally.
    }
}
