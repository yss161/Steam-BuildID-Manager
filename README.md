# Steam BuildID Manager

[![build](https://github.com/yss161/Steam-BuildID-Manager/actions/workflows/build.yml/badge.svg)](https://github.com/yss161/Steam-BuildID-Manager/actions/workflows/build.yml)

一个只做 Steam ACF 配置与 BuildID 管理的 Windows 桌面工具：扫描 Steam 库里的 `appmanifest_*.acf`，对比公开分支的最新 BuildID，并支持单个或一键批量更新。

> **原仓库**：[Clearlove0923/SteamCN-GameLauncher](https://github.com/Clearlove0923/SteamCN-GameLauncher)
>
> 本项目基于原项目修改，去掉了首页动画、资讯、截图、时长统计、外观设置、Python 采集和启动器自动更新等非 Steam 功能，只保留 Steam 配置、BuildID 检测与一键更新。原项目采用 MIT 许可证，本项目继续沿用并保留原作者版权声明。

## 功能

1. **扫描 Steam 库**：自动读取 `steamapps\appmanifest_*.acf`，显示游戏名称、AppID、当前 BuildID、安装目录和库位置。
2. **BuildID 更新检测**：从多个来源读取 `public` 分支，同时比较 `buildid`、每个 Depot 的 `manifest` 和 `StateFlags` 更新标志，标记“已是最新 / 发现更新 / 来源滞后 / 检测失败”。
3. **多数据源（四层）**：默认按「本机 Steam 缓存 → Steam 直连（SteamKit2 匿名 PICS）→ SteamCMD → api.steamcmd.net」取值，某个来源失败会自动回退，且 5 分钟内不再重试。设置页可切换优先级，并显示每个配置实际使用的来源与查询时间。
4. **内置 SteamCMD**：安装包直接携带 Valve 官方 `steamcmd.exe`，首次检测时自动部署到 `%LocalAppData%\SteamBuildIDManager\steamcmd`，无需手动下载；设置页仍提供「更新 / 删除 / 测试 / 打开目录」。
5. **版本证据校验**：写入前比对 Steam 自己记录的 `TargetBuildID` 与已安装 BuildID。若数据源落后于 Steam 已知版本，会标记为“来源滞后”并拒绝写入，避免把 Steam 的更新意图抹掉后又被覆盖（这是“更新后又变回去”的根因）。
6. **一键更新 BuildID**：支持单个更新和全部更新。同步 `buildid`、清空 `TargetBuildID`、刷新每个 Depot 的 `manifest`、清除 `StateFlags` 的“需要更新”标志，其余字段、缩进和换行符保持不变，更新后 Steam 不再提示更新。
7. **自动备份**：每次写入前把原 ACF 备份到 `%LocalAppData%\SteamBuildIDManager\backups\<AppID>\`，默认保留最近 5 份。
8. **新建 / 重建配置**：填写 AppID 后自动获取名称、安装目录、BuildID、Depot、启动 EXE 候选和 Steam 占位文件名，创建空占位 EXE 并写入 ACF，同时生成可粘贴到 Steam 启动选项的命令。
9. **Steam 路径探测**：读取注册表和 `libraryfolders.vdf`，自动发现 Steam 安装目录和所有库文件夹，也支持手动添加。
10. **本地账号读取**：从 `config\loginusers.vdf` 读取本机登录过的账号，按 `MostRecent` / `AutoLogin` / 时间排序，自动填充当前账号的 SteamID64；多账号时可弹窗选择，文件缺失时从 `userdata` 的 SteamID32 换算。
11. **外观设置**：主题支持跟随系统 / 浅色 / 深色；可选择本地图片作为窗口背景，并调整 10%–100% 不透明度。
12. **管理员启动**：设置页可开启“以管理员身份启动”，之后每次启动都会自动请求 UAC 提权，也可随时“立即以管理员重启”；取消 UAC 时仍以标准权限继续运行，不会崩溃。
13. **获取游戏信息**：输入 AppID 即可查看显示名称、安装目录名、BuildID、主 DepotID、主 Manifest、Steam 占位文件名、启动 EXE 和全部 Depot；确认后可一键「创建为新配置」直接生成 ACF 与占位文件。
14. **运行日志**：页面底部实时显示扫描、检测、更新和网络请求过程，可清空或打开日志文件；日志同时写入 `%LocalAppData%\SteamBuildIDManager\logs\app.log`。
15. **安全保护**：默认只列出 `SizeOnDisk = 0` 的占位配置；Steam 正在运行时给出警告；检测到 ACF 已被其他程序修改时拒绝覆盖。

## 界面与使用流程

1. 打开「设置」，确认 Steam 安装路径和库文件夹；SteamID64 为空时会自动读取本地最近登录账号，也可以点「读取本地账号」手动选择。
2. 回到「BuildID 管理」，点击「检测 BuildID 更新」。
3. 状态为“发现更新”的配置可以直接点「更新」，或点「一键更新全部」。
4. 更新完成后按提示完全重启 Steam，让它重新读取 ACF。
5. 新建配置时点击「新建配置」，填写 AppID 后离开输入框会自动获取 Steam 信息，核对后保存。生成的启动命令会自动复制到剪贴板，请粘贴到 Steam 库 → 游戏属性 → 启动选项。
6. 也可以点「获取游戏信息」，输入 AppID 后点「获取信息」查看元数据；确认无误后点「创建为新配置」即可直接生成 ACF。只配置了一个 Steam 库时直接写入，多个库时会弹出已预填好的确认框。

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

1. 每次推送或提交 Pull Request 时，自动运行 156 项核心回归测试。
2. 测试通过后，在 `windows-latest` 上以 `win-x64` 自包含方式发布 WinUI 3 应用。
3. 用 Inno Setup 生成免提权的用户级安装包（与原项目一致），并在 CI 中执行静默安装、payload/注册表检查和静默卸载冒烟测试。
4. 便携版 `Steam-BuildID-Manager-win-x64.zip` 和安装版 `Steam-BuildID-Manager-Setup-1.9.2.exe` 都会上传到 Actions Artifacts。
5. 推送 `v*` 标签时，两个产物会自动附加到 GitHub Release。

下载方式：

3. **安装版**：从 [Releases](https://github.com/yss161/Steam-BuildID-Manager/releases) 下载 `Steam-BuildID-Manager-Setup-1.9.2.exe`。
4. **便携版**：从 Releases 下载 `Steam-BuildID-Manager-win-x64.zip`，解压后直接运行。
5. **最新构建**：打开 [Actions](https://github.com/yss161/Steam-BuildID-Manager/actions/workflows/build.yml)，选择最近一次成功的 `build`，在页面底部下载安装版或便携版。

## 安装版

1. 双击 `Steam-BuildID-Manager-Setup-1.9.2.exe` 即可，**不会弹出 UAC 提权提示**。
2. 默认安装到 `%LocalAppData%\Programs\SteamBuildIDManager`（当前用户目录，无需管理员权限）；开始菜单和桌面快捷方式都属于当前用户，桌面快捷方式默认不勾选。
3. 安装程序会检测并关闭正在运行的旧版本，安装完成后可直接启动。
4. 卸载不需要管理员权限，入口在 Windows“设置 → 应用 → 已安装的应用”，不会删除用户设置和 ACF 备份。
5. 安装包界面支持简体中文和英文，按系统语言自动选择，使用 Windows 11 风格向导。
6. 需要给所有用户安装时，可在安装向导首页选择“为所有用户安装”，此时才需要管理员权限。
7. 安装开始前会检查临时目录是否可写；不可写时会直接说明位置和处理办法，而不是只报一句“没有写入权限”。

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
src/SteamBuildIdManager.Core/   与 UI 无关的 ACF 解析、BuildID 检测、Steam 账号读取、备份与写入
src/SteamBuildIdManager.App/    WinUI 3 界面、设置页和配置对话框
tests/SteamBuildIdManager.Core.Tests/  无第三方测试框架的回归测试
installer/                      Inno Setup 安装包脚本与简体中文语言文件
.github/workflows/build.yml     GitHub Actions 云编译（测试、便携版、安装版、Release）
```

核心测试覆盖：ACF 解析与字段保留、BuildID 数值比较、缺失 BuildID 插入、备份保留策略、扫描与批量更新、陈旧扫描拒绝、Steam 信息解析、Steam 本地账号 VDF 解析、SteamID32 换算、主题规范化、背景不透明度范围、主 Depot 选取、占位文件名推断、日志缓冲、库路径解析、配置生成、设置读写和 HTTP 取消。

## 数据来源与版本校验

默认按以下顺序取值，前一个不可用或没有该 App 的数据时自动尝试下一个：

2. **本机 Steam 缓存**（`appcacheppinfo.vdf`）：解析格式 39/40/41，离线可用，反映 Steam 客户端自己认为的版本。
3. **Steam 直连**（SteamKit2 3.4.0 匿名 PICS）：向 Steam 查询公开产品信息，每次独立会话、25 秒超时，使用进程内随机机器标识，不读取也不上传真实硬件 ID。
4. **SteamCMD**：调用官方 `app_info_print`，可在设置页一键安装。
5. **api.steamcmd.net**：第三方镜像，作为最后兜底，界面会标注它不保证实时。

仲裁规则：每个候选必须是完整快照（BuildID + 名称 + 安装目录 + 启动项 + 全部适用 Depot Manifest 齐全），且不能与 Steam 记录的版本证据冲突；剩余候选取 BuildID 最大者，绝不跨来源拼接字段。

写入前的强制检查（`SteamManifestUpdateGuard`）：

1. ACF 的 `TargetBuildID` 非 0 且与候选不一致 → 判定“来源滞后”，拒绝写入。
2. 已安装 BuildID 高于候选 → 拒绝，防止自动回退。
3. Steam 正在运行 → 拒绝写入，请先完全退出。

> 这些检查只说明“没有已知冲突”，不保证实时最新。来源本身也可能滞后，界面显示的查询时间是接收时间，不是发布时间。

## 安装包无法使用临时目录

如果双击安装包报：

```
安装程序无法创建目录 "C:\Users\…\Temp\is-XXXXXXXX.tmp"。
错误 5: 拒绝访问。
```

**原因**：Inno Setup 的安装程序会先把自身解压到 `%TEMP%\is-XXXX.tmp` 才能运行，这一步发生在**任何安装脚本代码之前**。所以只要这台机器的临时目录写不进去（权限被改坏、磁盘满、或安全软件拦截），安装包就会在启动阶段直接失败，**安装程序内部改什么都救不了**。

**解决办法**：用随附的启动器。

1. 把 `Start-Setup.cmd` 和 `Start-Setup.ps1` 与安装包放在**同一个文件夹**（便携版 zip 的 `installer\` 目录里已经带好了）。
2. 双击 `Start-Setup.cmd`。
3. 启动器会依次尝试这些位置，用「真正创建一个文件夹」来验证可写性：
   1. `%LocalAppData%\SteamBuildIDManager\InstallTemp`（程序本来就写这里，最可能可用）
   2. 系统 `TEMP` / `TMP`
   3. 安装包所在目录下的 `Temp`
   4. 系统盘根目录的 `SBIM-Temp`
4. 选中后把 `TEMP`/`TMP` 指向它，再启动安装程序 —— **向导界面与正常安装完全一样**。
5. 如果四个位置都不可写，启动器会列出每个位置的失败情况，并给出后续建议（改用便携版 zip 等）。

> 注意：不要用「关闭系统防护」来绕过这个错误，那会把所有软件的风险一起承担。启动器只是换了一个可写的临时目录，不修改任何系统安全设置。

## SmartScreen 提示说明

首次运行时会看到「Windows 已保护你的电脑 / Microsoft Defender SmartScreen 阻止了无法识别的应用启动」。

**原因**：这是 Windows 对「从网络下载 + 没有代码签名 + 尚无下载信誉」的程序的标准提示，与你本机的安全设置无关，只关系到这个文件本身有没有签名。它出现在安装包启动之前，所以安装程序内部做的任何处理（包括安装完成后自动解除文件锁定）都无法阻止它。

**三种处理方式**：

1. **直接继续**：点「更多信息」→「仍要运行」。这是官方给出的正常途径，仅需一次。
2. **用便携版**：下载 `Steam-BuildID-Manager-win-x64.zip` 后，先右键 zip →「属性」→ 勾选「解除锁定」，再解压运行 `SteamBuildIDManager.exe`。
3. **消除提示**：只有给程序签名才能彻底解决，见下方「代码签名」。

> 提示内容里的「发布者：发布者未知」就是「没有签名」的直接体现。任何号称能不改签名就永久消除此提示的说法，本质上都是在让你关闭系统防护，不建议采用。

### 代码签名

彻底消除提示需要有效的 Authenticode 证书。本项目已内置签名管线 `installer/sign.ps1`，配置证书后本地构建和 GitHub Actions 都会自动签名并校验：

1. **免费（推荐开源项目）**：[SignPath Foundation](https://signpath.org/) 为开源项目免费提供签名，证书以其基金会名义签发，可证明二进制确实来自本仓库的构建。
2. **付费 EV 证书**：SmartScreen 对 EV 证书立即给予信誉，提示立刻消失，但需要企业资质和较高的年费。
3. **付费 OV 证书**：价格较低，但**仍需累积下载量**才能让 SmartScreen 停止提示，不能立刻解决。

配置方式（GitHub 仓库 → Settings → Secrets → Actions）：

1. 添加 `SIGNING_PFX_BASE64`：PFX 证书文件的 Base64 文本。
2. 添加 `SIGNING_PFX_PASSWORD`：PFX 密码。
3. 或者改用 `SIGNING_THUMBPRINT`：证书已安装到构建机证书存储时使用。

未配置任何证书时，构建照常成功，产物保持未签名，日志会明确说明这一点。

本地签名：

```powershell
pwsh -File installer/sign.ps1 -PfxPath mycert.pfx -PfxPassword 你的密码
```

`steamcmd.exe` 由 Valve 官方签名，签名脚本会自动跳过它，不会覆盖他人签名。

## 常见问题

**Q：检测失败怎么办？**
A：先确认网络能访问 `api.steamcmd.net`。检测失败不会修改任何文件；也可以手动在“重建配置”里填写 BuildID。

**Q：更新后 Steam 还提示更新？**
A：完全退出 Steam（包括托盘）再启动；如果仍然提示，说明公开分支 BuildID 已再次变化，重新检测即可。

**Q：会不会修改真实游戏？**
A：本工具只写 `appmanifest_*.acf`，并只在占位文件不存在时创建 0 字节 EXE，不会覆盖真实游戏 EXE，也不修改游戏文件或内存。

**Q：默认列表里为什么没有我的游戏？**
A：默认只显示 `SizeOnDisk = 0` 的占位配置。打开“显示全部 Steam 游戏”可以查看所有 ACF，但对真实安装的游戏请谨慎操作。

**Q：安装包需要管理员权限吗？**
A：不需要。默认按当前用户安装到 `%LocalAppData%\Programs\SteamBuildIDManager`，安装和卸载都不提权。只有在向导里主动选择“为所有用户安装”时才需要管理员权限。

**Q：双击安装包报「安装程序无法创建目录 … 错误 5: 拒绝访问」怎么办？**
A：把 `Start-Setup.cmd` 和 `Start-Setup.ps1` 放到安装包同一个文件夹，双击 `Start-Setup.cmd`。它会先挑一个真正可写的临时目录再启动安装程序，**你看到的仍然是正常的安装向导页面**。详见「安装包无法使用临时目录」。

**Q：开启“以管理员身份启动”后没反应？**
A：启动时会弹出 UAC 提示，取消后应用会以标准权限继续运行，不会崩溃。也可以随时在设置页点“立即以管理员重启”。

**Q：背景图片会被复制吗？**
A：不会。程序只记录图片的绝对路径并直接读取；图片被移动或删除后会自动隐藏背景层，界面照常使用。

**Q：运行时弹出“Windows 已保护你的电脑”怎么办？**
A：点「更多信息」→「仍要运行」即可；便携版可先解锁 zip 再解压。这是未签名程序的固定提示，出现在安装包启动之前，程序内部无法阻止，只有代码签名能彻底消除，详见「SmartScreen 提示说明」。

**Q：安装包需要联网下载 SteamCMD 吗？**
A：不需要。安装包已内置 Valve 官方 `steamcmd.exe`，首次检测时会自动部署到 `%LocalAppData%\SteamBuildIDManager\steamcmd`。该副本仍会在首次运行时联网做自更新（Valve 的机制），若网络受限请改用「仅在线接口」，或先手动解压 `steamcmd.zip` 再用“浏览”指定路径。

**Q：SteamCMD 下载失败怎么办？**
A：程序会依次尝试 `media.steampowered.com` 与 `steamcdn-a.akamaihd.net`，单个地址 90 秒超时后自动换下一个。都被拦截时可先用“仅在线接口”，或手动下载 `steamcmd.zip` 解压后用“浏览”指定路径。

## 免责声明

1. 本工具通过修改 Steam 官方 ACF 文件实现配置，可能不符合 Steam 服务条款，请自行评估风险。
2. 本工具不会修改游戏文件或内存数据，也不保证不会触发账号风控。
3. 使用本工具产生的任何问题，开发者不承担责任。
4. 请支持正版游戏。

## 许可证

本项目沿用原项目的 MIT 许可证，详见 [LICENSE](LICENSE)。原项目版权归 Violet0923 所有。