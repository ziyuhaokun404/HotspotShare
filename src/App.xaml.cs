using HotspotShare.Services;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Wpf.Ui.Appearance;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace HotspotShare;

public partial class App : Application
{
    internal static AppLogService Logs { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        base.OnStartup(e);
        Logs = new AppLogService(GetLogDirectory());
        Logs.Initialize();
        Logs.WriteInformation("应用程序启动。", "Application", nameof(App),
            details: $"Version={System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}; PID={Environment.ProcessId}",
            eventId: "app.startup");
        ApplicationThemeManager.Apply(ApplicationTheme.Dark);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Logs.WriteInformation("应用程序退出。", "Application", nameof(App), eventId: "app.exit");
        Logs.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logs.WriteError("程序发生未处理异常。", "Application", nameof(App), e.Exception, eventId: "app.dispatcher-unhandled");
        MessageBox.Show(
            $"程序发生未处理异常：{e.Exception.Message}{Environment.NewLine}{Environment.NewLine}日志位置：{Logs.GetCurrentLogFilePath()}",
            "HotspotShare",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnCurrentDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            Logs.WriteError("程序发生 AppDomain 未处理异常。", "Application", nameof(App), exception, eventId: "app.domain-unhandled");
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Logs.WriteError("程序发生未观察到的任务异常。", "Application", nameof(App), e.Exception, eventId: "app.task-unobserved");
        e.SetObserved();
    }

    private static string GetLogDirectory()
    {
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
    }
}
