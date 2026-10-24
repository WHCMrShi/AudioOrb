# 性能记录

测试环境：

- Windows 11 Build 26300，x64
- 2560×1440，100% 缩放
- Release 自包含构建，桌面组件显示、设置窗口关闭、鼠标移开
- 采样 20 秒

结果：

| 指标 | 数值 |
| --- | --- |
| 空闲 CPU | **0.078%**（单核百分比） |
| 工作集 | **约 140–170 MB** |
| 私有内存 | **约 95–125 MB** |
| 线程 | 约 64 |
| 句柄 | 约 1,100 |

开发期 Debug 构建、刚启动、未打开设置时的工作集约 **136 MB**，私有内存约 **95 MB**。打开并关闭设置后，WinUI 的合成器/运行时缓存会让工作集停留在约 **160 MB**。

## 为什么不是更低

WinUI 3 桌面应用的基线主要来自：

- .NET 8 运行时；
- `Microsoft.ui.xaml.dll`、WinUI 控件与合成器；
- Windows App SDK 的窗口/输入/资源基础设施；
- DWM 合成表面。

一个空白 WinUI 3 窗口在本机已约 120 MB 工作集。项目自身没有引入后台服务、常驻音频轮询、视频/WebView 或大型第三方库。

## 已做的优化

- 音频设备变化使用 `IMMNotificationClient`，不轮询。
- 切换/枚举在后台线程，UI 线程只更新状态。
- 桌面组件使用单个 16 ms timer（仅按住鼠标拖动时）和一个 180 ms hover timer。
- 设置窗口关闭时销毁，而不是长期隐藏。
- Release 发布裁剪未使用的 Windows App SDK AI/WebView2/Widgets 负载，目录从 208 MB 降至约 160 MB。
- 不播放常驻动画；只有切换时 170 ms 的淡入/位移和 `ProgressRing`。

## 复现测量

```powershell
$p = Get-Process AudioOrb | Select-Object -First 1
$cpu0 = $p.CPU
$t0 = Get-Date
Start-Sleep 20
$p.Refresh()
$elapsed = ((Get-Date) - $t0).TotalSeconds
"CPU% = {0:N3}" -f (($p.CPU - $cpu0) / $elapsed * 100)
"WS MB = {0:N1}" -f ($p.WorkingSet64 / 1MB)
"Private MB = {0:N1}" -f ($p.PrivateMemorySize64 / 1MB)
```

测量时请关闭设置窗口并把鼠标移出桌面组件；第一次启动/首次 JIT 会抬高 CPU，建议运行 1 分钟后再测。
