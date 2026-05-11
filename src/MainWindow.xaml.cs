using HotspotShare.Models;
using HotspotShare.Pages;
using HotspotShare.ViewModels;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace HotspotShare;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly MainWindowViewModel _viewModel = new();
    private readonly PendingPrivilegedAction? _pendingAction = PendingPrivilegedAction.TryLoadFromCommandLine(Environment.GetCommandLineArgs().Skip(1));
    private System.Windows.Forms.NotifyIcon? _notifyIcon;
    private Icon? _notifyIconAsset;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;

        _viewModel.RequestElevation = OnRequestElevation;
        _viewModel.RequestAlert = OnRequestAlert;
        _viewModel.RequestConfirm = OnRequestConfirm;
        _viewModel.PropertyChanged += ViewModelOnPropertyChanged;

        InitializeNotifyIcon();
        NavigateToCurrentPage();
    }

    private void InitializeNotifyIcon()
    {
        _notifyIconAsset = LoadApplicationIcon();
        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Text = "HotspotShare",
            Icon = _notifyIconAsset,
            Visible = false
        };

        var contextMenu = new System.Windows.Forms.ContextMenuStrip();
        contextMenu.Items.Add("显示窗口", null, (_, _) => RestoreFromTray());
        contextMenu.Items.Add("退出", null, (_, _) =>
        {
            _notifyIcon.Visible = false;
            System.Windows.Application.Current.Shutdown();
        });
        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.DoubleClick += (_, _) => RestoreFromTray();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized)
        {
            Hide();
            if (_notifyIcon is not null)
            {
                _notifyIcon.Visible = true;
            }
        }
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.PropertyChanged -= ViewModelOnPropertyChanged;
        _viewModel.Dispose();

        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        _notifyIconAsset?.Dispose();
        _notifyIconAsset = null;

        base.OnClosed(e);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        NavigateToCurrentPage();
        await _viewModel.InitializeAsync(_pendingAction);
    }

    private void NavItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Wpf.Ui.Controls.NavigationViewItem item || item.Tag is not string pageTag)
        {
            return;
        }

        var navigationItem = _viewModel.NavigationItems.FirstOrDefault(candidate =>
            candidate.PageTag.Equals(pageTag, StringComparison.OrdinalIgnoreCase));
        if (navigationItem is not null)
        {
            _viewModel.SelectedNavigationItem = navigationItem;
            return;
        }

        _viewModel.CurrentPageTag = pageTag;
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.CurrentPageTag))
        {
            NavigateToCurrentPage();
        }
    }

    private void NavigateToCurrentPage()
    {
        if (ContentFrame.Content is Page currentPage &&
            currentPage.Tag is string currentTag &&
            currentTag.Equals(_viewModel.CurrentPageTag, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var page = CreatePage(_viewModel.CurrentPageTag);
        page.Tag = _viewModel.CurrentPageTag;
        page.DataContext = _viewModel;
        ContentFrame.Navigate(page);
        UpdateNavigationSelection(_viewModel.CurrentPageTag);
    }

    private Page CreatePage(string pageTag)
    {
        return pageTag switch
        {
            "devices" => new DevicesPage(),
            "logs" => new LogsPage(),
            "about" => new AboutPage(),
            _ => new DashboardPage()
        };
    }

    private void UpdateNavigationSelection(string pageTag)
    {
        DashboardNavItem.IsActive = pageTag.Equals("dashboard", StringComparison.OrdinalIgnoreCase);
        DevicesNavItem.IsActive = pageTag.Equals("devices", StringComparison.OrdinalIgnoreCase);
        LogsNavItem.IsActive = pageTag.Equals("logs", StringComparison.OrdinalIgnoreCase);
        AboutNavItem.IsActive = pageTag.Equals("about", StringComparison.OrdinalIgnoreCase);
    }

    private void OnRequestAlert(string title, string message)
    {
        MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private bool OnRequestConfirm(string title, string message)
    {
        return MessageBox.Show(this, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private void OnRequestElevation(string actionName, PendingPrivilegedAction pendingAction)
    {
        var confirmed = MessageBox.Show(
            this,
            $"{actionName}需要管理员权限。是否现在重新以管理员身份启动程序？",
            "需要管理员权限",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmed != MessageBoxResult.Yes)
        {
            return;
        }

        string? pendingActionFile = null;

        try
        {
            var executablePath = ResolveExecutablePath();
            pendingActionFile = PendingPrivilegedAction.WriteToTemporaryFile(pendingAction);
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = AppContext.BaseDirectory,
                Arguments = PendingPrivilegedAction.BuildArguments(pendingActionFile),
                UseShellExecute = true,
                Verb = "runas"
            };

            Process.Start(startInfo);
            Close();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            PendingPrivilegedAction.TryDelete(pendingActionFile);
        }
        catch (Exception ex)
        {
            PendingPrivilegedAction.TryDelete(pendingActionFile);
            MessageBox.Show(this, $"请求管理员权限失败：{ex.Message}", "提权失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string ResolveExecutablePath()
    {
        var currentProcessPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(currentProcessPath) &&
            !Path.GetFileName(currentProcessPath).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            return currentProcessPath;
        }

        var candidate = Path.Combine(AppContext.BaseDirectory, "HotspotShare.exe");
        if (File.Exists(candidate))
        {
            return candidate;
        }

        throw new FileNotFoundException("找不到可用于提权重启的程序文件。", candidate);
    }

    private static Icon LoadApplicationIcon()
    {
        var resourceInfo = Application.GetResourceStream(
            new Uri("pack://application:,,,/HotspotShare;component/Assets/icon.ico", UriKind.Absolute));

        if (resourceInfo is null)
        {
            return (Icon)SystemIcons.Application.Clone();
        }

        using var stream = resourceInfo.Stream;
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }
}
