# HotspotShare

Windows 移动热点共享工具。通过 WinRT API 管理系统移动热点，支持选择共享连接、设置 SSID / 密码 / Wi-Fi 频段（2.4 GHz / 5 GHz），一键启动或停止热点。

## 功能

- 枚举系统网络连接，选择要共享的来源
- 自定义热点名称、密码、Wi-Fi 频段
- 实时显示热点状态和连接设备数
- 非管理员运行时自动提示 UAC 提权
- 深色 / 浅色主题切换
- 最小化到系统托盘
- 自动轮询刷新热点状态

## 技术栈

- .NET 8 / WPF
- [WPF-UI](https://github.com/lepoco/wpfui) 4.1.0（Fluent / WinUI 风格控件）
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) 8.4.0
- Windows PowerShell 5.1 调用 WinRT `NetworkOperatorTetheringManager` API

## 项目结构

```
HotspotShare/
├── HotspotShare.slnx
└── src/
    ├── HotspotShare.csproj
    ├── App.xaml / App.xaml.cs
    ├── MainWindow.xaml / MainWindow.xaml.cs
    ├── Converters/
    ├── Models/
    ├── Services/
    └── ViewModels/
```

## 构建与运行

```bash
dotnet build HotspotShare.slnx
dotnet run --project src/HotspotShare.csproj
```

> 启动 / 停止热点需要管理员权限，程序会在需要时自动提示提权。

## 系统要求

- Windows 10 2004+ / Windows 11
- 支持移动热点的无线网卡
