# 更新记录

## v1.6.0 — 2026-10-27

### 更新功能

1. SteamCMD 支持一键配置：新增「一键安装」「更新」「删除」「打开目录」按钮，自动下载 Valve 官方 `steamcmd.zip` 到本机应用目录并解压，不修改系统 PATH 或注册表。
2. 下载支持两个官方镜像（`media.steampowered.com` 与 `steamcdn-a.akamaihd.net`），单个地址 90 秒超时后自动换下一个；安装完成后自动做一次首次自更新检测。
3. 修复「以管理员身份重启」点了没反应：重启前先释放单实例互斥锁，避免提权后的新进程误判“已有实例”而直接退出；取消 UAC 时会重新取回锁并继续以标准权限运行。
4. 缓解 SmartScreen「Windows 已保护你的电脑」：安装包在安装完成时自动解除文件锁定（`Unblock-File`），便携版压缩包内附带 `Unblock.cmd` 一键解除；不修改任何系统安全设置。
5. 新增 6 项回归测试，覆盖 SteamCMD 下载/解压/删除与镜像回退，核心测试总数增至 135 项。

### 说明

1. 彻底消除 SmartScreen 提示需要代码签名证书（EV/OV），这属于签名成本而非软件设置，程序本身无法绕过。
2. SteamCMD 首次运行仍需联网自更新；若被网络或证书拦截，界面会提示原因并自动回退到在线接口。
## v1.5.0 — 2026-10-27

### 更新功能

1. 新增多数据源：保留原有 `api.steamcmd.net` 在线接口，并加入 **SteamCMD**（官方 `app_info_print`）作为第二个来源。
2. 设置页新增「数据来源」：可选“优先 SteamCMD / 优先在线接口 / 仅在线接口 / 仅 SteamCMD”，并可指定或自动检测 `steamcmd.exe`。
3. 新增「测试 SteamCMD」按钮，一键验证 steamcmd 能否启动与匿名登录。
4. 数据源自动回退：设为“优先 SteamCMD”时，若 SteamCMD 不可用会自动改用在线接口，扫描不会中断。
5. 失败熔断：SteamCMD 调用失败后 5 分钟内不再重试，避免每个 App 都等一次超时；单个 App 无公开数据不会禁用整个来源。
6. 新增 12 项回归测试，覆盖 VDF 解析、SteamCMD 输出解析、来源回退、熔断与设置往返，核心测试总数增至 129 项。

### 说明

1. SteamCMD 首次运行需要联网自更新；被网络或证书拦截时会提示原因并回退。
2. SteamCMD 读取的是 Steam 官方 PICS 数据，通常比第三方镜像更新。
## v1.4.0 — 2026-10-27

### 更新功能

1. 「获取游戏信息」新增“创建为新配置”按钮：填入 AppID 获取成功后，一键即可生成 appmanifest 与占位 EXE。
2. 只配置了一个 Steam 库时直接写入，不再弹出二次表单；配置了多个库时弹出已预填好的新建对话框，只需选择目标库并确认。
3. 「新建配置」中输入 AppID 后离开输入框即自动获取 Steam 信息，无需再点按钮。
4. 自动创建沿用完整校验：显示名称、安装目录名、BuildID、Depot、占位文件名缺一不可，写入前仍会备份。
5. 自动创建的结果会写入运行日志，便于回溯。

### 说明

1. 自动创建需要先在设置中配置 Steam 安装路径、至少一个库文件夹和 SteamID64。
2. 生成的配置与原项目一致：`StateFlags=4`、`DownloadType=1`、无待下载字节、`TargetBuildID=0`。
## v1.3.2 — 2026-10-27

### 修复

1. 修复“更新后再打开 Steam 又变回去”：Steam 启动时会继续执行 ACF 里残留的下载队列。现在把更新状态整体复位，与新建配置生成的结果一致。
2. `StateFlags` 直接写为 `4`（已完全安装）。1.3.1 只清除“需要更新”位，518 会变成 516，仍保留 512（更新已暂停），Steam 因此继续提示更新。
3. 同时清零 `BytesToDownload`、`BytesDownloaded`、`BytesToStage`、`BytesStaged`、`StagingSize`，并把 `DownloadType` 改为 `1`、`TargetBuildID` 与 `ScheduledAutoUpdate` 归零。
4. 检测逻辑改为识别整套“未复位”状态（非 4 的 StateFlags、非 0 的 TargetBuildID、存在待下载字节、DownloadType=4），并在提示中列出具体字段，例如“更新状态未复位（StateFlags=518、TargetBuildID=26236512、待下载 2.76 GB）”。
5. 新增 2 项回归测试，覆盖暂停下载状态的复位，核心测试总数增至 117 项。

### 说明

1. 这一版的状态字段写法与原项目生成的结果一致，可避免 Steam 覆盖修改。
2. 更新前仍需完全退出 Steam；更新后建议先确认 ACF 已写入再打开 Steam。

## v1.3.1 — 2026-10-21

### 修复

1. 修复“更新后 Steam 仍提示需要更新”：更新时不再只改 `buildid`，同时同步 `InstalledDepots` 中每个 Depot 的 `manifest`，并补齐 ACF 里缺失的 Depot。
2. 修复 `StateFlags` 的“需要更新”标志位（bit 2）未被清除的问题，更新后与 Steam 认为已安装的状态一致。
3. 检测逻辑同时比较 `buildid`、Depot Manifest 和 `StateFlags`，三者任一不一致都会标记为“发现更新”，并在提示中说明具体原因。
4. 更新确认框和运行日志会说明本次同时完成 BuildID、Depot Manifest 与 StateFlags 修复。
5. 新增 13 项回归测试，覆盖 Depot Manifest 同步、缺失 Depot 补全、StateFlags 修复与漂移检测，核心测试总数增至 115 项。

### 说明

1. 更新前请完全退出 Steam，否则客户端会用内存中的旧状态覆盖修改。
2. 更新仍会自动备份原 ACF，备份位置与之前一致。

## v1.3.0 — 2026-10-20

### 更新功能

1. 新增“运行日志”面板：扫描、检测、更新、配置写入和网络请求都会实时记录，支持清空和一键打开日志文件。
2. 日志同时写入 `%LocalAppData%\SteamBuildIDManager\logs\app.log`，窗口关闭后仍可回溯。
3. Steam 占位文件名改为随 AppID 自动获取：优先使用 appinfo 记录的启动 EXE 文件名，其次回退到安装目录名。
4. 「获取游戏信息」对话框新增“Steam 占位文件名”字段。
5. 新增 8 项回归测试，覆盖占位名推断与日志缓冲，核心测试总数增至 102 项。

### 说明

1. 占位文件名必须与 Steam appinfo 记录的可执行文件名一致，否则 Steam 无法启动该配置。
2. 日志只记录路径和结果，不记录账号密码等敏感信息。

## v1.2.0 — 2026-10-20

### 更新功能

1. 安装包改为自动申请管理员权限：`PrivilegesRequired=admin`，默认安装到 `Program Files`，快捷键写入所有用户开始菜单，安装与卸载都需要 UAC 确认。
2. 新增主题切换：设置页“外观”可选择跟随系统、浅色或深色；深色模式同步应用 Mica 背景与标题栏配色。
3. 新增背景图片：支持 PNG/JPG/BMP/WebP/GIF，可调 10%–100% 不透明度，并叠加随主题变化的遮罩保证文字可读。
4. 新增“以管理员身份启动”开关：开启后每次启动自动请求 UAC 提权，可直接“立即以管理员重启”；提权失败时保留标准权限继续运行。
5. 新增“获取游戏信息”对话框：输入 AppID 后展示显示名称、安装目录名、BuildID、主 DepotID、主 Manifest、启动 EXE 和全部 Depot，并可复制启动命令。
6. 新增 15 项回归测试，覆盖主题规范化、不透明度范围、设置往返、主 Depot 选取与缺失信息告警，核心测试总数增至 94 项。

### 说明

1. 安装包升级为全机安装；从 1.1.0 升级时会保留原有设置与 ACF 备份。
2. 背景图片只读取本地文件，不复制、不上传；文件被删除时自动隐藏背景层而不影响启动。
3. “获取游戏信息”为只读功能，不会写入或修改任何 ACF。

## v1.1.0 — 2026-10-20

### 更新功能

1. 新增本地 Steam 账号读取：解析 `config\loginusers.vdf`，自动填充当前账号的 SteamID64，无需手动查询。
2. 支持多账号：按 `MostRecent`、`AutoLogin` 和登录时间排序，设置页可点击“读取本地账号”弹窗选择。
3. 当 `loginusers.vdf` 缺失或损坏时，从 `userdata` 下的 SteamID32 目录名换算 SteamID64。
4. 设置页 SteamID64 为空时自动读取最近登录账号，同时保留手动输入和修改能力。
5. 新增 6 项账号解析回归测试，核心测试总数增至 79 项。
6. 版本升级到 1.1.0，安装包与便携版同步更新。

### 说明

1. 账号信息只从本机 Steam 文件读取，不会上传或写入日志。
2. 读取结果仅用于填充 ACF 的 `LastOwner`，只更新 BuildID 时仍然可以不填。

## v1.0.1 — 2026-10-20

### 更新功能

1. 新增 Inno Setup 安装版：按用户安装到 `%LocalAppData%\Programs\SteamBuildIDManager`，不请求管理员权限。
2. 安装版创建开始菜单快捷方式，可选桌面快捷方式，并在安装前自动关闭正在运行的旧版本。
3. 安装包提供简体中文和英文界面，卸载入口写入 Windows“已安装的应用”，不会删除用户设置与 ACF 备份。
4. GitHub Actions 新增安装包构建与静默安装/卸载冒烟测试，Release 同时提供便携版 zip 和安装版 exe。

### 说明

1. 安装版与便携版使用同一份自包含发布产物，功能完全一致。
2. 安装包使用 Inno Setup 6 构建，简体中文语言文件来自 Inno Setup 官方仓库。

## v1.0.0 — 2026-10-20

### 更新功能

1. 项目精简为只保留 Steam ACF 配置与 BuildID 相关功能，移除首页动画、资讯、截图、时长统计、外观、Python 采集和启动器自动更新。
2. 新增 BuildID 更新检测：扫描 Steam 库中的 `appmanifest_*.acf`，对比 `api.steamcmd.net` 公开分支 BuildID。
3. 新增单个与批量“一键更新 BuildID”，写入前自动备份，只替换 `buildid` 并清空 `TargetBuildID`。
4. 新增配置新建与重建流程：获取 Steam 信息后自动填充名称、安装目录、Depot、BuildID 和启动 EXE 候选。
5. 新增 Steam 安装路径与库文件夹自动探测、SteamID64 设置、Steam 运行中警告和陈旧 ACF 拒绝覆盖。
6. 新增 GitHub Actions 云编译：先运行 73 项核心回归测试，再发布 win-x64 自包含压缩包并上传 Artifact。

### 说明

1. 公开 BuildID 依赖第三方接口 `https://api.steamcmd.net`，接口不可用时只影响自动检测。
2. 本项目基于 [SteamCN-GameLauncher](https://github.com/Clearlove0923/SteamCN-GameLauncher) 修改，沿用 MIT 许可证。
