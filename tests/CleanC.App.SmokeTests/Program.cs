using System.Windows;
using System.Windows.Interop;
using CleanC.App;

namespace CleanC.App.SmokeTests;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        try
        {
            var application = new App
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown
            };
            application.InitializeComponent();
            var window = new MainWindow();
            application.MainWindow = window;
            window.Show();
            window.Measure(new Size(1440, 900));
            window.Arrange(new Rect(0, 0, 1440, 900));
            window.UpdateLayout();

            if (window.DataContext is null)
            {
                throw new InvalidOperationException("MainWindow DataContext was not initialized.");
            }
            if (!window.Title.Contains("CleanC", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("MainWindow title was not initialized.");
            }
            if (!window.IsVisible || new WindowInteropHelper(window).Handle == IntPtr.Zero)
            {
                throw new InvalidOperationException("MainWindow did not create a visible native window.");
            }

            window.Close();
            application.Shutdown();
            Console.WriteLine("PASS  MainWindow XAML loaded and completed layout.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FAIL  MainWindow smoke test");
            Console.Error.WriteLine(exception);
            return 1;
        }
    }
}
