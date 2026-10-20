# 更新记录

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