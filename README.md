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

- .NET 10 / WPF
- [WPF-UI](https://github.com/lepoco/wpfui) 4.2.0（Fluent / WinUI 风格控件）
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) 8.4.2
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

需要先安装 .NET 10 SDK。

```bash
dotnet build HotspotShare.slnx
dotnet run --project src/HotspotShare.csproj
```

> 启动 / 停止热点需要管理员权限，程序会在需要时自动提示提权。

## 单文件发布

仓库内置了一个 PowerShell 脚本用于生成自包含的单文件发布包，默认输出到 `artifacts/publish-singlefile/<RID>/`。

默认发布 `win-x64`：

```powershell
powershell -ExecutionPolicy Bypass -File .\script\publish-singlefile.ps1
```

发布 `win-arm64`：

```powershell
powershell -ExecutionPolicy Bypass -File .\script\publish-singlefile.ps1 -Architecture arm64
```

也可以直接指定完整 Runtime Identifier：

```powershell
powershell -ExecutionPolicy Bypass -File .\script\publish-singlefile.ps1 -Runtime win-arm64
```

默认会移除 `.pdb`，发布目录通常只保留一个 `HotspotShare.exe`。如需保留调试符号：

```powershell
powershell -ExecutionPolicy Bypass -File .\script\publish-singlefile.ps1 -KeepSymbols
```

## Release 打包

`script\release.ps1` 会在单文件发布的基础上，统一完成版本号解析、清理旧输出、归档 exe 和生成 zip 压缩包。输出目录为 `artifacts/release/<Version>/`。

显式指定版本号并打包 `win-x64`：

```powershell
powershell -ExecutionPolicy Bypass -File .\script\release.ps1 -Version 1.0.0
```

打包 `win-arm64`：

```powershell
powershell -ExecutionPolicy Bypass -File .\script\release.ps1 -Version 1.0.0 -Architecture arm64
```

如果不传 `-Version`，脚本会优先尝试读取最近一个 git tag；若仓库还没有 tag，则回退为时间戳版本，例如 `0.0.0-dev.20260417-104500`。

## 系统要求

- Windows 10 2004+ / Windows 11
- 支持移动热点的无线网卡
