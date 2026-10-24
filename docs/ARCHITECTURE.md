# 架构设计

## 项目分层

```text
AudioOrb (WinUI 3)
  ├─ MainWindow              桌面卡片、拖拽、点击、悬停、右键操作视图
  ├─ SettingsWindow          设置窗口、Mica、导航
  ├─ AppHost                 设置、设备缓存、切换串行化、通知去抖
  └─ Services
       ├─ FullscreenWatcher  前台全屏检测（WinEvent + 单次延迟复检）
       ├─ ThemeService       浅色/深色、标题栏颜色
       └─ WindowEffects      Win32 样式、圆角、置顶、透明度

AudioSourceSelect.Core
  ├─ AudioDeviceCatalog      COM 枚举与默认设备切换
  ├─ AudioDeviceMonitor      设备/默认设备变更通知
  ├─ DeviceSwitchPlanner     纯逻辑：循环顺序
  └─ SettingsStore           JSON 设置持久化
```

## 音频切换

### 枚举

`IMMDeviceEnumerator.EnumAudioEndpoints(eRender, DEVICE_STATE_ACTIVE)` 枚举活动输出端点。每个端点读取：

- `PKEY_Device_FriendlyName`：`扬声器 (Realtek(R) Audio)`
- `PKEY_DeviceInterface_FriendlyName`：适配器名
- `PKEY_AudioEndpoint_FormFactor`：扬声器/耳机/HDMI 等

`PROPVARIANT` 在 x64 上是 **24 字节**（8 字节头 + 16 字节 DECIMAL union）。项目中的 `PropVariant` 显式设置 `Size = 24`，否则 COM 写入会越界并导致属性读取异常。

### 切换

使用未公开的 `IPolicyConfig::SetDefaultEndpoint`，对三个角色分别设置：

- `eConsole`
- `eMultimedia`
- `eCommunications`

设置后在后台线程用新的 enumerator 轮询最多约 200 ms，确认默认设备真的变了，避免把 `S_OK` 误判为成功。设备切换在 `Task.Run` 中执行，完成后回 UI 线程刷新。

### 通知

`AudioDeviceMonitor` 注册 `IMMNotificationClient`：

- 设备增删/状态变化 → 刷新列表；
- 默认设备变化 → 只处理 render + console 角色，避免一次变更收到 3 次通知；
- 回调来自 COM 线程，统一通过 `DispatcherQueue.TryEnqueue` 回 UI 线程，并用短去抖合并连续事件。

### 切换顺序

`DeviceSwitchPlanner` 是无 COM/无 UI 的纯函数：

```text
当前设备在启用列表中的下一项，循环；
当前设备不在列表 -> 第一项；
direction = -1 -> 上一项。
```

自动配对优先选择“当前设备 + 不同类型设备”，并优先耳机的 Game 通道而不是 Chat 通道。

## 桌面组件窗口

目标不是覆盖游戏的悬浮层，而是 **Windows 7 桌面小工具**式组件：只出现在桌面上，普通应用会覆盖它；按 Win+D 显示桌面时要能出现；点击不抢焦点。

### 桌面层与 Win+D

实现方式：

1. 窗口使用 `WS_POPUP | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED`，不设置 `WS_EX_TOPMOST`；
2. 正常状态下用 `SetWindowPos(hwnd, Progman/Shell, ...)` 把卡片放在桌面之上、普通窗口之下；
3. 180 ms hover timer 同时判断前台窗口：
   - 前台是桌面/任务栏时，临时 `HWND_TOPMOST`，让卡片通过 Win+D 的显示桌面状态；
   - 前台是普通应用/游戏/开始菜单时，移除 topmost 并放回桌面层，应用会覆盖它；
4. 卡片保持 `WS_EX_NOACTIVATE`，点击切换音频不会把焦点从游戏或编辑器抢走。

这比“始终置顶”更接近 Win7 小工具，也避免覆盖游戏画面。

### 为什么仍保留分层窗口

桌面组件模式已经尝试过：

- 非分层窗口 + 正常窗口边框；
- `MicaBackdrop`；
- `DesktopAcrylicController`（带 `IsInputActive`）；
- 直接 `DWMWA_SYSTEMBACKDROP_TYPE` + `DwmExtendFrameIntoClientArea`。

WinUI 3 对这个无标题栏工具窗口仍会把系统材质退化为实色表面，因此按 [Fluent 2 Material](https://fluent2.microsoft.design/material) 的降级原则改用 **Solid 材质**：实色和 elevation 建立层级，不保留半透明、噪点、装饰性描边或顶部装饰线。Hover 不是激活；即使临时强制激活，该窗口仍不会渲染 Mica/Acrylic，因此不做 hover 材质切换。

### 样式与边缘

- 无边框 `WS_POPUP`，移除 `WS_DLGFRAME/WS_CAPTION`；
- DWM `DWMWA_WINDOW_CORNER_PREFERENCE = ROUND`；
- `WS_EX_LAYERED` 仅用于可靠的桌面层/topmost 行为，alpha 固定 255，不承担材质效果；
- 深色：`#292929` 表面；浅色：`#FAFAFA`；
- 8 px 圆角，由 DWM 窗口圆角形成外轮廓；
- 卡片内部 1 px Fluent contour（`CardStrokeColorDefaultBrush`），对应 Windows Layering 文档中 Card / elevation 8 / stroke 1；
- 不叠加自定义分层阴影窗口。WinUI 顶层窗口的 DWM 系统阴影无法通过 `DWMWA_NCRENDERING_POLICY`、`DwmExtendFrameIntoClientArea` 或移除 `WS_EX_LAYERED` 关闭；再叠加自定义阴影会产生双重阴影，并使文字随阴影强度变暗；
- 因此只保留一层系统窗口阴影，卡片本身仍为纯 Solid，无内部渐变/高光，也不提供阴影强度设置；
- 不使用噪声贴图、顶部高光带或强调色渐变。

### 尺寸与布局

一个滑杆（0–3）同时控制 footprint 和内容布局：

- **0 / 图标**：56 × 56，隐藏文字、音量和辅助图标，仅保留 24 px 设备图标；音量视图只显示百分比，不显示细条；
- **1 / 紧凑**：168 px，显示设备主名称，副标题改为「音量 xx%」，右侧不重复显示音量；音量视图左右排列百分比和细条，并隐藏喇叭图标；
- **2 / 标准**：236 px，显示名称、适配器，以及右侧音量百分比和 3 × 34 px **竖向**音量条（与左侧两行内容同高）；
- **3 / 宽**：280 px，布局与标准相同。

设备图标使用 Segoe Fluent Icons 线条图标。设备类别来自 endpoint form factor 与 `PKEY_Device_ContainerId`：同一 PnP 容器里只要有 Headset endpoint，该容器的 Game/Chat 两个 endpoint 都使用耳麦图标；不依赖设备名或 AI 关键字。

大小只提供一个四档滑杆；阴影不提供设置，由系统窗口阴影与 1 px contour 统一表达层级。

### 输入

WinUI 在这个窗口上不能稳定派发 hover 指针事件，拖拽的 `PointerMoved` 也不可靠。项目做了两个兜底：

- 按住左键期间启动 16 ms 的光标监视 timer，用 `GetCursorPos` 移动窗口；松开后吸附并保存位置；
- 180 ms 的 timer 只负责桌面层/topmost 状态切换和 Win+D 适配；不驱动悬停变色或弹窗。

两个 timer 都只在对应状态需要时运行。音量由 1 s 的轻量 timer 读取默认输出 endpoint。滚轮默认调整音量，可在设置中改为切换设备；音量模式用 `IAudioEndpointVolume.SetMasterVolumeLevelScalar` 每次调整 2%，纯音量视图会一直保持，直到鼠标点击或离开卡片再淡出恢复，避免快速/慢速滚动导致自动恢复时机不一致。音量视图已显示时继续滚动只更新数值，不重播切换动画。

## 设置窗口

- `ExtendsContentIntoTitleBar + SetTitleBar`，Mica 背景；
- 页面是普通 `Page`，通过 `Frame.Navigate(type, AppHost)` 注入 host；
- 主题切换会同时更新元素主题、Mica 配置、DWM 深色模式与标题栏按钮颜色；
- 在 Mica 上叠加约 20% 的中性 scrim；当显式主题与系统主题不一致或 Mica 不受支持时 scrim 变为完全不透明，保证文字可读性；
- `ThemedMicaBackdrop` 自行维护 `SystemBackdropConfiguration`，因此覆盖并跳过 `OnDefaultSystemBackdropConfigurationChanged`；
- 设备行只保留设备图标、名称/描述、右侧启用开关；不再提供顺序箭头或自动配对入口。设备列表稳定排序，切换默认设备时只更新「当前」徽标；
- 系统托盘菜单使用 Win32 原生菜单，沿用 Windows 11 对系统菜单的材质与圆角处理；
- `Assets` 通过 csproj 的 `None Update` 复制到输出与发布目录，确保 `App.ico` 和 `Square48Logo.png` 在自包含发布中仍存在；
- 设置卡片左侧使用 18 px 裸 FontIcon（无圆角底板、Primary 颜色），右侧 ContentPresenter 统一右对齐；设备行开关使用 52 px 固定宽度贴到卡片右轴，开启时圆点在右。

## 全屏检测

`FullscreenWatcher` 使用 `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)`：

- 前台窗口覆盖整个 monitor（容差 2 px）时判定为全屏；
- 排除桌面、任务栏、shell 和自己的进程；
- 游戏常常“先变前台、后变可见/全屏”，因此每次前台变化后额外做一次 250 ms 的单次复检；
- 隐藏/显示使用 `ShowWindow(SW_SHOWNOACTIVATE)`，不抢焦点。

## 托盘图标

`TrayIcon` 使用 Win32 `Shell_NotifyIcon`：

- 一个消息窗口接收 `WM_APP + 0x31` 回调；
- 左键 / 双击打开设置，右键弹出原生 `TrackPopupMenuEx` 菜单；
- 菜单提供切换设备、设置、隐藏/显示桌面组件、开机启动、退出；
- 添加图标后将 `HKCU\Control Panel\NotifyIconSettings` 中匹配当前 EXE 的 `IsPromoted` 设为 1，避免 Windows 11 默认把新图标收进溢出菜单；
- 监听 `TaskbarCreated`，Explorer 重启后重新注册图标；
- 退出时 `NIM_DELETE` 并销毁图标句柄。

应用图标由 `tools/generate_assets.py` 生成：Fluent 蓝色圆角方块、白色扬声器和双向切换徽标，同时输出多尺寸 ICO、PNG 和应用清单资源。

## 单实例

使用 `Local\AudioOrb.SingleInstance.v1` 命名 Mutex。第二个实例不会直接 `HWND_BROADCAST`（无法可靠到达 owned window），而是用 `EnumWindows` 枚举顶层窗口并直接 `PostMessage` 注册消息；第一个实例收到后唤起设置窗口。1.5 秒内重复消息会去重。

## 设置

`SettingsStore` 使用 `System.Text.Json`：

- 原子写（临时文件 + `File.Move`）；
- 读取时归一化范围与枚举；
- 损坏文件备份为 `settings.json.invalid-*`；
- 滑块的连续变化使用 280 ms 防抖写入，避免每次像素移动都写盘。

## 线程与资源

- UI 线程只做轻量状态更新；音频枚举/切换在 `Task.Run` 中执行；
- 所有 COM 对象在 `finally` 中释放；
- 事件驱动优先；唯一常驻 timer 是 180 ms hover 探针；
- 设置关闭即销毁窗口，不保留页面对象。
