using System.IO;
using System.Text;
using System.Windows.Threading;
using System.Windows;
using Wpf.Ui.Appearance;

namespace HotspotShare;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        base.OnStartup(e);
        ApplicationThemeManager.Apply(ApplicationTheme.Light);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteExceptionLog("DispatcherUnhandledException", e.Exception);
        MessageBox.Show(
            $"程序发生未处理异常：{e.Exception.Message}{Environment.NewLine}{Environment.NewLine}日志位置：{GetLogPath()}",
            "HotspotShare",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnCurrentDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            WriteExceptionLog("CurrentDomainUnhandledException", exception);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteExceptionLog("UnobservedTaskException", e.Exception);
        e.SetObserved();
    }

    private static void WriteExceptionLog(string source, Exception exception)
    {
        try
        {
            var path = GetLogPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var builder = new StringBuilder();
            builder.AppendLine(new string('=', 72));
            builder.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            builder.AppendLine(source);
            builder.AppendLine(exception.ToString());

            File.AppendAllText(path, builder.ToString(), Encoding.UTF8);
        }
        catch
        {
        }
    }

    private static string GetLogPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HotspotShare",
            "logs",
            "app.log");
    }
}
