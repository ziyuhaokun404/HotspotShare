# HotspotShare 测试现状分析

> 报告日期：2026-06-15
> 范围：`tests/HotspotShare.Tests/` 全部内容
> 方法：静态阅读 + `dotnet test` 实跑

## 一、当前测试的全貌

```
tests/HotspotShare.Tests/
├── HotspotShare.Tests.csproj     # xUnit 2.9.3 + coverlet
└── AppLogServiceTests.cs         # 唯一测试文件
```

| 维度 | 数据 |
|---|---|
| 测试文件数 | 1 |
| 测试类数 | 1（`AppLogServiceTests`） |
| 测试用例数 | 3 |
| 覆盖的源代码文件 | 1 / 16（约 6%） |
| 跑测时间 | 245 ms |
| 当前结果 | ✅ 3/3 通过 |

## 二、三个测试在测什么

| 测试 | 验证点 |
|---|---|
| `WriteInformationAsync_PersistsStructuredLogEntryToJsonFile` | `WriteInformationAsync` 把日志落到 `*.jsonl` 文件，JSON 包含 Level/Category/Message/Source |
| `InitializeAsync_LoadsRecentEntriesAndKeepsOnlyConfiguredMemoryLimit` | `Entries` 超过 `memoryLimit` 时只保留最新 N 条；下次 `InitializeAsync` 从磁盘加载仍能恢复这个上限 |
| `WriteErrorAsync_PersistsEventIdDetailsAndException` | `WriteErrorAsync` 把 `exception.ToString()`、`details`、`eventId` 都序列化进 JSON |

## 三、测试解决了什么问题

**核心价值**：把 `AppLogService` 从"看起来在写文件"变成"可验证的契约"。

具体保证了三件事：

1. **JSON 形状稳定** — `Logs` 页面、文本快照、第三方排障脚本都依赖这个格式不被无声改变
2. **Memory 限流行为** — `Entries` 是 `ObservableCollection` 绑定到 UI，超限会撑爆内存
3. **跨进程可读** — `InitializeAsync` 重启时把昨天日志加载回内存，这是审计/取证的关键

## 四、测试覆盖的盲区

按"测试价值 × 实施难度"分类。

### 🟥 高度重要 + 容易测（强烈建议补）

| 模块 | 行数 | 为什么重要 | 测试难度 |
|---|---|---|---|
| **`PendingPrivilegedAction`** | 142 | UAC 提权流程的协议：base64 编码路径、camelCase JSON、`--pending-action-file=` 参数解析、文件不存在/损坏回退 | ⭐ 纯逻辑，无 IO 依赖（除 Temp 目录） |
| **`DeviceAliasStore`** | 51 | 设备备注名持久化，Dictionary 大小写不敏感键、空文件、JSON 损坏回退、原子写 | ⭐ 纯 IO |
| **`AppPreferences`** | 51（刚加的） | 主题偏好持久化；同 DeviceAliasStore 的可靠性要求 | ⭐ 纯 IO |
| **`GlobalExceptionGuard`** | 130（刚加的） | 异常落地后是否调用了 `Shutdown`；`Interlocked` 防重入；写日志失败时不二次抛 | ⭐⭐ 需要 mock `Application` 或测公共副作用 |
| **`TetheringClientInfo` 派生属性** | 42 | `IpAddressDisplay`、`ConnectionState`、`ConnectedDurationText` 等格式化逻辑被 Logs 页和 Devices 页 UI 直接依赖 | ⭐ |

### 🟧 重要 + 中等难度

| 模块 | 为什么 | 难点 |
|---|---|---|
| **`MainWindowViewModel`** | 业务核心：状态映射、`ApplyStatus` 数据流、设备去重/排序/60s 保留、`ValidateInputs` 密码规则、`CreatePassphrase` 长度 | 需要把 9 个 RelayCommand 依赖（`TetheringService` / `AppLogService` / `DeviceAliasStore`）抽成接口才能 mock |
| **`StatusEntry` / `UpdateConnectedClients` 客户端排序** | 排序规则（已连接+有 IP > 已连接 > 刚断开）写死在 `GetClientSortOrder`，改坏后 Logs/Devices 页面顺序错乱 | ⭐⭐ 同上 |
| **`InverseBooleanToVisibilityConverter` / `InverseBooleanConverter`** | 极简但若改坏会让空态文案整页错位 | ⭐ |

### 🟨 UI 层（价值高 / 成本高）

- `DashboardPage` 窄屏堆叠 / `LogsPage` 窄屏堆叠：需要 WPF UI 自动化（FlaUI）或纯 ViewModel 层抽离
- `MainWindowViewModel.ToggleTheme` 主题切换：可测 ViewModel 状态变更，但 `ApplicationThemeManager.Apply` 是静态副作用
- 密码强度评估（`PassphraseStrength`）的评分规则：纯函数，最值得测；目前规则在 VM 内部，应抽成静态方法 `PasswordStrength.Evaluate(string)`

### 🟦 不可测或成本远大于价值

- `TetheringService` — 调用 WinRT `NetworkOperatorTetheringManager`，需真实 Windows + 移动热点硬件支持
- XAML 渲染 — 需要 UI 自动化框架

## 五、测试本身的小毛病

1. **`[Fact]` + `IDisposable`**：3 个测试用同一份 temp 目录（`Dispose` 在每个 case 结束时清理），case 之间**有顺序耦合风险** — 虽然每个 case 都用新 `AppLogService` 实例写同一目录，如果某 case 跑完没清理（异常路径），下一个会读到残留。当前 `Dispose` 兜底，**勉强 OK**，但更稳的写法是 `IAsyncLifetime` 或 `IDisposable` + `[Collection("...")]`。
2. **没断言"轮转按天"**：`GetCurrentLogFilePath` 用了 `DateTime.Now:yyyyMMdd`，跨日期行为是隐含契约但没测。
3. **没断言"过期清理"**：`retentionDays: 7` 删旧文件逻辑没测。
4. **没断言"非英文 Category/EventId"** 转义问题：JSON 写入是 UTF-8 但没 case 验证中文 / emoji。
5. **没断言"`AppendAllText` 原子性"**：高并发下 200 个并发 `WriteAsync` 会不会交错？目前用 `lock(_syncRoot)` 串行化了，但没人验证。
6. **没覆盖"`Service` 在 `InitializeAsync` 之前就 `Write`"** 的边缘情况（看代码是允许的，因为 `WriteAsync` 不会调用 `InitializeAsync` 里的东西）。

## 六、与 `.gitignore` / CI 的关系

- `bin/`、`obj/` 在 `.gitignore` 里
- 没有 `.github/workflows/` 之类的 CI 配置文件 — 改完没人自动跑
- 既然有 `tests/` 目录 + `HotspotShare.slnx` 把两个项目都纳入了，**最小补一个 CI**（GitHub Actions `dotnet test`）就能防止回归

## 七、要不要现在补？我的建议

按"性价比"分两批。

### 批 1（半天，0 风险）：纯函数 + 纯 IO

- `PendingPrivilegedAction.TryLoadFromCommandLine` 5 个 case（无参数 / 有效 / 文件不存在 / 损坏 / 编码异常）
- `DeviceAliasStore` 3 个 case（空 / 写后读回 / 损坏回退空字典）
- `AppPreferences` 同上
- `PasswordStrength.Evaluate` 抽出来后 5 个 case（空 / 弱 / 中 / 强 / 边界长度）

### 批 2（一天，需要小重构）：ViewModel 关键路径

- 把 `MainWindowViewModel` 依赖的 3 个服务抽成接口 `ITetheringService` / `IAppLogService` / `IDeviceAliasStore`
- 测 `ApplyStatus` 状态映射、`UpdateConnectedClients` 排序与去重、`ValidateInputs` 边界、`CreatePassphrase` 长度
- 测 `SaveClientAliasCommand` 写盘 + 刷新

跑完这两批，**核心业务逻辑覆盖率能从 ~6% 升到 ~50%**，且 `style/refactor-pass-1` 后续的 UI 调整不会静默回归。

## 八、一句话总结

> 项目测试当前是 **"有比没有强，但远不够"** 的状态 — 1 个文件 3 个 case 守住了 `AppLogService` 这一个基础设施的契约，但 `MainWindowViewModel`（1000+ 行业务核心）、`PendingPrivilegedAction`（UAC 协议）、`DeviceAliasStore` / `AppPreferences`（用户数据持久化）这些**一旦改坏就立刻出 production 问题**的模块完全裸奔。**建议优先把 `PendingPrivilegedAction` 和两个 Store 补上**（半天工作量，回报很高）。

---

## 附录 A：跑测命令

```bash
dotnet test tests/HotspotShare.Tests/HotspotShare.Tests.csproj
```

预期输出：

```
总共 1 个测试文件与指定模式相匹配。
已通过! - 失败:     0，通过:     3，已跳过:     0，总计:     3，持续时间: 245 ms
```

## 附录 B：源代码规模参考

| 模块 | 行数 | 是否有测试 |
|---|---:|:---:|
| `ViewModels/MainWindowViewModel.cs` | 1188 | ❌ |
| `Services/TetheringService.cs` | — | ❌（WinRT 依赖） |
| `Services/AppLogService.cs` | — | ✅ |
| `Services/DeviceAliasStore.cs` | 51 | ❌ |
| `Services/AppPreferences.cs` | 51 | ❌ |
| `Services/GlobalExceptionGuard.cs` | 130 | ❌ |
| `Models/PendingPrivilegedAction.cs` | 142 | ❌ |
| `Models/TetheringClientInfo.cs` | 42 | ❌ |
| `Models/AppLogEntry.cs` | 33 | ❌ |
| `Models/StatusEntry.cs` | 16 | ❌ |
| `Models/AppNavigationItem.cs` | 14 | ❌ |
| `Models/TetheringActionResult.cs` | 10 | ❌ |
| `Models/TetheringConnectionProfile.cs` | 14 | ❌ |
| `Models/TetheringStatus.cs` | 22 | ❌ |
| **业务代码总计** | **约 1900+** | **<10%** |
