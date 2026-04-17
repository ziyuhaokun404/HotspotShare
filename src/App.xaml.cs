using System.IO;
using System.Windows;
using System.Windows.Threading;
using HotspotShare.Services;
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
        Logs.InitializeAsync().GetAwaiter().GetResult();
        ApplicationThemeManager.Apply(ApplicationTheme.Dark);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Logs.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logs.WriteErrorAsync("程序发生未处理异常。", "Application", nameof(App), e.Exception, eventId: "app.dispatcher-unhandled")
            .GetAwaiter()
            .GetResult();
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
            Logs.WriteErrorAsync("程序发生 AppDomain 未处理异常。", "Application", nameof(App), exception, eventId: "app.domain-unhandled")
                .GetAwaiter()
                .GetResult();
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Logs.WriteErrorAsync("程序发生未观察到的任务异常。", "Application", nameof(App), e.Exception, eventId: "app.task-unobserved")
            .GetAwaiter()
            .GetResult();
        e.SetObserved();
    }

    private static string GetLogDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HotspotShare",
            "logs");
    }
}
