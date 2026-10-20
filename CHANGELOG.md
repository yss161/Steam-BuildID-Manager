# 更新记录

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