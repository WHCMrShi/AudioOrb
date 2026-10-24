# 开发与调试

## 环境

- Windows 11 x64
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- VS Code + 推荐扩展（C# Dev Kit、C#、PowerShell）
- 不需要完整 Visual Studio。WinUI 3 的 XAML 编译器和 Windows SDK 引用由 NuGet 还原。

验证：

```powershell
dotnet --version     # 8.x
dotnet --list-sdks
```

## 构建与运行

```powershell
dotnet build .\AudioOrb.sln -c Debug
powershell -ExecutionPolicy Bypass -File .\scripts\run.ps1
```

解决方案使用 `Any CPU` 配置，内部把 `AudioOrb` 映射到 `x64`；直接构建项目时也可以使用：

```powershell
dotnet build .\src\AudioOrb\AudioOrb.csproj -c Debug -p:Platform=x64
```

Debug 输出是框架依赖的。如果直接双击 `bin\x64\Debug\...\AudioOrb.exe` 提示缺少 .NET Desktop Runtime，请用 `scripts\publish.ps1` 生成自包含包，或安装 .NET 8 Desktop Runtime。

## VS Code

打开仓库根目录后：

- `Ctrl+Shift+B`：默认构建任务。
- `F5`：启动 `AudioOrb (Debug)`，调试器附加到输出目录的 exe。
- `Terminal > Run Task > publish`：发布自包含 x64 包。

`.vscode/launch.json` 中的路径是固定的 Debug 输出路径，修改 `TargetFramework` 后需要同步修改。

## 音频探针

`tests/AudioProbe` 不依赖 UI，可用来验证 Core Audio 行为：

```powershell
$exe = ".\tests\AudioProbe\bin\Debug\net8.0-windows10.0.19041.0\AudioProbe.exe"

& $exe list       # 列出活动输出设备、ID、当前默认
& $exe switch 2   # 切换到列表第 2 台
& $exe toggle     # 切到另一台
& $exe roundtrip  # 切换后再切回，验证往返
& $exe settings   # 设置归一化冒烟测试
& $exe monitor    # 监听设备变化，Ctrl+C 退出
```

`roundtrip` 会实际变更系统默认输出设备，但会恢复原设备。

## 配置与日志

```text
%LOCALAPPDATA%\AudioOrb\settings.json
%LOCALAPPDATA%\AudioOrb\logs\app.log
```

从设置页「关于」可以直接打开这两个目录。日志最多约 512 KB，超出后轮转为 `app.log.old`。

## 发布

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish.ps1
```

输出：

```text
dist\AudioOrb-win-x64\
dist\AudioOrb-win-x64.zip
```

脚本会：

- 以 Release、`win-x64`、self-contained 方式发布；
- 移除未使用的 AI / WebView2 / Widgets 负载；
- 复制 README 与 LICENSE；
- 生成 zip。

发布后请在没有设置 `DOTNET_ROOT` 的终端中启动一次，确认自包含运行时正常。

## Git / GitHub

仓库已经初始化为 `main`，并带有 `v1.0.0` 标签。登录 GitHub CLI 后可直接一键存档：

```powershell
gh auth login --hostname github.com --git-protocol https --web
powershell -ExecutionPolicy Bypass -File .\scripts\push-github.ps1
```

脚本会读取账号信息、创建公开仓库 `AudioOrb`、推送 `main` 与标签，并在 zip 存在时创建 Release。私有仓库使用 `-Visibility Private`。

不要提交 `bin/`、`obj/`、`dist/`、`artifacts/`、日志或本机设置文件；`.gitignore` 已处理。

手动推送：

```powershell
git remote add origin <repo-url>
git push -u origin main
git push origin --tags
```
