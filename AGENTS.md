# Steam BuildID Manager 开发规则

本文件适用于整个仓库。项目只保留 Steam ACF 配置与 BuildID 相关功能。

## 功能边界

- 仅处理 Steam `appmanifest_*.acf`：扫描、解析、BuildID 检测、BuildID 更新、配置生成和备份。
- 不引入首页动画、资讯、截图、时长统计、外观设置、厂商 Provider、Python Worker 或启动器自动更新。
- 不修改游戏文件、游戏内存或 Steam 客户端二进制。
- 界面、网络和文件写入必须解耦：UI 只调用 `SteamBuildIdManager.Core` 中的服务。

## 项目结构

```text
src/SteamBuildIdManager.Core/   纯逻辑：ACF、Steam 信息、路径、备份、设置
src/SteamBuildIdManager.App/    WinUI 3：页面、对话框、选择器和展示模型
tests/SteamBuildIdManager.Core.Tests/  无第三方框架的回归测试
```

- 新增文件写入必须放在 `ManifestFileService`，先备份、再临时文件、最后原子替换。
- BuildID 更新只替换 `buildid` 并清空 `TargetBuildID`，不得重新序列化整个 ACF。
- 数值比较使用 `ulong`，禁止使用 `int`、`double` 或字符串排序比较 BuildID。
- 默认只展示 `SizeOnDisk = 0` 的占位配置；对真实安装的游戏必须由用户显式开启“显示全部”。
- 公开 BuildID 读取 `api.steamcmd.net` 的 `public` 分支；接口失败只影响检测，不阻塞手动填写。

## 安全与可靠性

- Steam 正在运行时，更新前必须提示用户先完全退出 Steam。
- 扫描后 ACF 被其他程序改写时，必须拒绝覆盖并要求刷新。
- 占位 EXE 已存在时不得覆盖，避免破坏真实游戏文件。
- 更新前必须备份到 `%LocalAppData%\SteamBuildIDManager\backups\<AppID>\`。
- 所有网络调用异步、可取消；UI 不得阻塞。

## 构建与测试

```powershell
dotnet run --project tests/SteamBuildIdManager.Core.Tests/SteamBuildIdManager.Core.Tests.csproj -c Release
dotnet publish src/SteamBuildIdManager.App/SteamBuildIdManager.App.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:WindowsAppSDKSelfContained=true -p:PublishReadyToRun=false -o publish
```

- 每个功能修复都必须补充对应回归测试。
- GitHub Actions 工作流必须保持“测试通过后才发布”的顺序。
- 云编译产物必须包含 `SteamBuildIDManager.exe` 和 PRI 资源，否则 WinUI 启动会崩溃。

## 版本文档

- `AppInfo.Version`、csproj `<Version>`、`Package.appxmanifest` Identity.Version 保持一致；tag 使用 `v` 前缀。
- 发布说明、CHANGELOG 和 Release 正文的条目使用从 `1.` 开始的有序序号，不使用 `-`、`*` 或 `+` 项目符号。
- 每次推送前在 `docs/PUSH_HISTORY.md` 顶部追加一条记录，只保留最近 5 条；记录时间、推送人、分支、实现内容和验证结果。
- README 必须保留原项目地址：<https://github.com/Clearlove0923/SteamCN-GameLauncher>。