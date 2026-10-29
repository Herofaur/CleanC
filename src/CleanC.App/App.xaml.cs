using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace CleanC.App;

public partial class App : Application
{
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CleanC",
        "logs");

    private static readonly string StartupLogPath = Path.Combine(LogDirectory, "startup.log");

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            WriteLog($"Starting CleanC {typeof(App).Assembly.GetName().Version} on {Environment.OSVersion}.");
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            var window = new MainWindow();
            MainWindow = window;
            window.Show();
            window.Activate();
            WriteLog("Main window created and shown.");
        }
        catch (Exception exception)
        {
            ShowFatalStartupError(exception);
            Shutdown(-1);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteLog("Dispatcher exception", e.Exception);
        MessageBox.Show(
            $"CleanC 遇到错误：\n\n{e.Exception.Message}\n\n日志：{StartupLogPath}",
            "CleanC 错误",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        WriteLog("Unhandled exception", e.ExceptionObject as Exception);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteLog("Unobserved task exception", e.Exception);
        e.SetObserved();
    }

    private static void ShowFatalStartupError(Exception exception)
    {
        WriteLog("Startup failed", exception);
        MessageBox.Show(
            $"CleanC 无法启动：\n\n{exception.Message}\n\n详细日志已写入：\n{StartupLogPath}",
            "CleanC 启动失败",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private static void WriteLog(string message, Exception? exception = null)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var builder = new StringBuilder()
                .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"))
                .Append("  ")
                .AppendLine(message);
            if (exception is not null)
            {
                builder.AppendLine(exception.ToString());
            }
            File.AppendAllText(StartupLogPath, builder.ToString(), Encoding.UTF8);
        }
        catch
        {
            // Logging must never prevent the application from starting.
        }
    }
}
