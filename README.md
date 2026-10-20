# Steam BuildID Manager

[![build](https://github.com/yss161/Steam-BuildID-Manager/actions/workflows/build.yml/badge.svg)](https://github.com/yss161/Steam-BuildID-Manager/actions/workflows/build.yml)

一个只做 Steam ACF 配置与 BuildID 管理的 Windows 桌面工具：扫描 Steam 库里的 `appmanifest_*.acf`，对比公开分支的最新 BuildID，并支持单个或一键批量更新。

> **原仓库**：[Clearlove0923/SteamCN-GameLauncher](https://github.com/Clearlove0923/SteamCN-GameLauncher)
>
> 本项目基于原项目修改，去掉了首页动画、资讯、截图、时长统计、外观设置、Python 采集和启动器自动更新等非 Steam 功能，只保留 Steam 配置、BuildID 检测与一键更新。原项目采用 MIT 许可证，本项目继续沿用并保留原作者版权声明。

## 功能

1. **扫描 Steam 库**：自动读取 `steamapps\appmanifest_*.acf`，显示游戏名称、AppID、当前 BuildID、安装目录和库位置。
2. **BuildID 更新检测**：从 SteamCMD 公开信息接口读取 `public` 分支 BuildID，与本地 ACF 做数值比较，标记“已是最新 / 发现更新 / 检测失败”。
3. **一键更新 BuildID**：支持单个更新和全部更新。只替换 ACF 中的 `buildid` 并清空 `TargetBuildID`，其余字段、缩进、换行符和 Depot 配置保持不变。
4. **自动备份**：每次写入前把原 ACF 备份到 `%LocalAppData%\SteamBuildIDManager\backups\<AppID>\`，默认保留最近 5 份。
5. **新建 / 重建配置**：填写 AppID 后自动获取名称、安装目录、BuildID、Depot 和启动 EXE 候选，创建空占位 EXE 并写入 ACF，同时生成可粘贴到 Steam 启动选项的命令。
6. **Steam 路径探测**：读取注册表和 `libraryfolders.vdf`，自动发现 Steam 安装目录和所有库文件夹，也支持手动添加。
7. **安全保护**：默认只列出 `SizeOnDisk = 0` 的占位配置；Steam 正在运行时给出警告；检测到 ACF 已被其他程序修改时拒绝覆盖。

## 界面与使用流程

1. 打开「设置」，确认 Steam 安装路径和库文件夹；需要新建配置时再填写 SteamID64。
2. 回到「BuildID 管理」，点击「检测 BuildID 更新」。
3. 状态为“发现更新”的配置可以直接点「更新」，或点「一键更新全部」。
4. 更新完成后按提示完全重启 Steam，让它重新读取 ACF。
5. 新建配置时点击「新建配置」，填写 AppID，点「获取 Steam 信息」，核对后保存。生成的启动命令会自动复制到剪贴板，请粘贴到 Steam 库 → 游戏属性 → 启动选项。

> 更新 BuildID 前建议先完全退出 Steam。Steam 运行时可能把内存中的旧配置写回 ACF，导致更新被覆盖。

## 工作原理

```text
Steam 库 appmanifest_*.acf
        │ 读取 name / appid / buildid / InstalledDepots / SizeOnDisk
        ▼
BuildIdUpdateService ──► api.steamcmd.net/v1/info/<AppID> ──► public.buildid
        │ 数值比较
        ▼
发现更新 ──► 备份原 ACF ──► 替换 buildid + TargetBuildID=0 ──► 原子写回
```

- BuildID 使用 `ulong` 数值比较，不按字符串比较，也不经过浮点数。
- 只修改目标两行，不重新序列化整个 ACF，避免破坏 `LauncherPath`、`UserConfig`、`MountedConfig` 等字段。
- 扫描结果和更新操作都会重新解析 ACF；如果磁盘上的 BuildID 与扫描时不一致，会拒绝覆盖并提示刷新。
- 公开 BuildID 来自第三方接口 `https://api.steamcmd.net`，接口不可用时只影响检测，不影响手动填写。

## 下载与 GitHub 云编译

仓库自带 [GitHub Actions 工作流](.github/workflows/build.yml)：

1. 每次推送或提交 Pull Request 时，自动运行 73 项核心回归测试。
2. 测试通过后，在 `windows-latest` 上以 `win-x64` 自包含方式发布 WinUI 3 应用。
3. 用 Inno Setup 生成按用户安装的安装包，并在 CI 中执行静默安装、启动文件检查和静默卸载冒烟测试。
4. 便携版 `Steam-BuildID-Manager-win-x64.zip` 和安装版 `Steam-BuildID-Manager-Setup-1.0.1.exe` 都会上传到 Actions Artifacts。
5. 推送 `v*` 标签时，两个产物会自动附加到 GitHub Release。

下载方式：

1. **安装版**：从 [Releases](https://github.com/yss161/Steam-BuildID-Manager/releases) 下载 `Steam-BuildID-Manager-Setup-1.0.1.exe`。
2. **便携版**：从 Releases 下载 `Steam-BuildID-Manager-win-x64.zip`，解压后直接运行。
3. **最新构建**：打开 [Actions](https://github.com/yss161/Steam-BuildID-Manager/actions/workflows/build.yml)，选择最近一次成功的 `build`，在页面底部下载安装版或便携版。

## 安装版

1. 双击 `Steam-BuildID-Manager-Setup-1.0.1.exe`，按向导完成安装，不需要管理员权限。
2. 默认安装到 `%LocalAppData%\Programs\SteamBuildIDManager`，并创建开始菜单快捷方式；桌面快捷方式默认不勾选。
3. 安装程序会检测并关闭正在运行的旧版本，安装完成后可直接启动。
4. 卸载入口在 Windows“设置 → 应用 → 已安装的应用”，不会删除用户设置和 ACF 备份。
5. 安装包界面支持简体中文和英文，按系统语言自动选择。

## 本地构建

环境要求：

1. Windows 10 1809 或更高版本（x64）。
2. .NET 8 SDK。
3. Visual Studio 2022 或 Windows SDK（WinUI 3 编译需要）。

```powershell
# 运行核心回归测试
dotnet run --project tests/SteamBuildIdManager.Core.Tests/SteamBuildIdManager.Core.Tests.csproj -c Release

# 编译并发布自包含版本
dotnet publish src/SteamBuildIdManager.App/SteamBuildIdManager.App.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:Platform=x64 -p:WindowsAppSDKSelfContained=true `
  -p:PublishReadyToRun=false -o publish

# 生成安装包（需要 Inno Setup 6）
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" installer\SteamBuildIDManager.iss
```

发布目录中的 `SteamBuildIDManager.exe` 可直接运行，不要求机器预装 Windows App SDK 运行时。安装包输出到 `artifacts\installer\`，构建说明见 [installer/README.md](installer/README.md)。

## 项目结构

```text
src/SteamBuildIdManager.Core/   与 UI 无关的 ACF 解析、BuildID 检测、备份与写入
src/SteamBuildIdManager.App/    WinUI 3 界面、设置页和配置对话框
tests/SteamBuildIdManager.Core.Tests/  无第三方测试框架的回归测试
installer/                      Inno Setup 安装包脚本与简体中文语言文件
.github/workflows/build.yml     GitHub Actions 云编译（测试、便携版、安装版、Release）
```

核心测试覆盖：ACF 解析与字段保留、BuildID 数值比较、缺失 BuildID 插入、备份保留策略、扫描与批量更新、陈旧扫描拒绝、Steam 信息解析、库路径解析、配置生成、设置读写和 HTTP 取消。

## 常见问题

**Q：检测失败怎么办？**
A：先确认网络能访问 `api.steamcmd.net`。检测失败不会修改任何文件；也可以手动在“重建配置”里填写 BuildID。

**Q：更新后 Steam 还提示更新？**
A：完全退出 Steam（包括托盘）再启动；如果仍然提示，说明公开分支 BuildID 已再次变化，重新检测即可。

**Q：会不会修改真实游戏？**
A：本工具只写 `appmanifest_*.acf`，并只在占位文件不存在时创建 0 字节 EXE，不会覆盖真实游戏 EXE，也不修改游戏文件或内存。

**Q：默认列表里为什么没有我的游戏？**
A：默认只显示 `SizeOnDisk = 0` 的占位配置。打开“显示全部 Steam 游戏”可以查看所有 ACF，但对真实安装的游戏请谨慎操作。

## 免责声明

1. 本工具通过修改 Steam 官方 ACF 文件实现配置，可能不符合 Steam 服务条款，请自行评估风险。
2. 本工具不会修改游戏文件或内存数据，也不保证不会触发账号风控。
3. 使用本工具产生的任何问题，开发者不承担责任。
4. 请支持正版游戏。

## 许可证

本项目沿用原项目的 MIT 许可证，详见 [LICENSE](LICENSE)。原项目版权归 Violet0923 所有。