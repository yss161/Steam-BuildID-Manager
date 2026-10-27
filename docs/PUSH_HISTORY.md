# 推送记录

## 2026-10-27 19:50:00 +08:00

1. 推送人员：圆鲨鲨
2. 目标分支：main
3. 实现内容：版本升级到 1.4.0；「获取游戏信息」新增“创建为新配置”，填入 AppID 获取成功后可直接生成 appmanifest 与占位 EXE；单库时直接写入、多库时弹出已预填的新建对话框；「新建配置」中输入 AppID 后失焦自动获取 Steam 信息。
4. 验证结果：117 项核心回归测试通过；用临时库实测“填入 4706890/4162040 → 获取信息 → 创建为新配置”，成功写出 `appmanifest_4162040.acf` 与 `common\Zenless Zone Zero\HYP.exe`，ACF 为 `StateFlags=4`、`DownloadType=1`、`Bytes*=0`、`TargetBuildID=0`，与新建配置生成结果一致；列表自动刷新为 1 个配置。
5. 说明：本机 nuget.org 解析被拦截，本地改用华为云 NuGet 镜像还原；CI 仍使用官方源。
## 2026-10-27 19:10:00 +08:00

1. 推送人员：圆鲨鲨
2. 目标分支：main
3. 实现内容：版本升级到 1.3.2；修复“更新后再打开 Steam 又变回去”。对照原项目 SteamCN-GameLauncher 的备份与改写结果，确认差异在更新状态字段：原项目把 `StateFlags` 直接写为 4、`DownloadType` 写为 1、`BytesToDownload`/`BytesDownloaded`/`BytesToStage`/`BytesStaged`/`StagingSize` 清零、`TargetBuildID` 与 `ScheduledAutoUpdate` 归零。本版照此整体复位，替换 1.3.1 仅清 bit 2 的做法（518 → 516 仍保留 512 更新暂停位，Steam 会继续下载并覆盖 ACF）。检测逻辑同步改为识别整套未复位状态并列出具体字段。
4. 验证结果：117 项核心回归测试通过；对真实 `appmanifest_4706890.acf` 做只读转换，输出 `StateFlags 518→4`、`DownloadType 4→1`、`BytesToDownload 2959529119→0`、`TargetBuildID 26236512→0`，字段集合与原项目生成结果一致；Core 与 App 均编译通过。
5. 说明：本机 nuget.org 解析被拦截，改用华为云 NuGet 镜像完成本地还原；CI 仍走官方源。本机沙箱无法写入 `%LocalAppData%\SteamBuildIDManager\backups`，UI 写入闭环由单元测试与真实 ACF 只读转换验证。
## 2026-10-21 13:30:00 +08:00

1. 推送人员：圆鲨鲨
2. 目标分支：main
3. 实现内容：版本升级到 1.3.1；修复“软件内更新 BuildID 后 Steam 仍提示需要更新”。更新流程从只改 `buildid` 扩展为同步 `InstalledDepots` 内每个 Depot 的 `manifest`、补齐缺失 Depot、清除 `StateFlags` 的“需要更新”标志位（bit 2）并把 `UpdateResult` 归零；检测逻辑同时比对 BuildID、Depot Manifest 与 StateFlags。
4. 验证结果：115 项核心回归测试通过；用真实 228980 配置复现并确认修复——其 `StateFlags` 为 6（含需要更新位）、BuildID 与公开分支一致，旧逻辑判为“已是最新”导致 Steam 继续提示更新，新逻辑正确标记为可修复；对真实 ACF 做只读转换验证 `StateFlags 6 → 4`、`UpdateResult → 0`、Depot Manifest 全部对齐。
5. 说明：更新写入前需完全退出 Steam；本机沙箱限制了对 `%LocalAppData%\SteamBuildIDManager\backups` 的写入，写入链路由单元测试与真实文件的只读转换验证。

## 2026-10-20 18:00:00 +08:00

1. 推送人员：圆鲨鲨
2. 目标分支：main
3. 实现内容：版本升级到 1.3.0；新增“运行日志”面板与 `app.log` 文件日志，覆盖扫描、检测、更新、配置写入和网络请求；Steam 占位文件名改为随 AppID 自动获取，优先取 appinfo 启动 EXE 文件名，回退到安装目录名；「获取游戏信息」新增占位文件名展示。
4. 验证结果：102 项核心回归测试通过；本机 UI Automation 验证日志面板实时显示 7 条扫描/检测/获取记录、`HYP.exe` 在 AppID 4162040 下自动填入占位文件名字段；1.3.0 自包含发布与 Inno Setup 安装包编译成功。
5. 说明：占位文件名必须与 Steam appinfo 记录一致，否则 Steam 无法启动配置。

## 2026-10-20 11:20:00 +08:00

1. 推送人员：圆鲨鲨
2. 目标分支：main
3. 实现内容：版本升级到 1.1.0；新增本地 Steam 账号读取，解析 `config\loginusers.vdf` 并按 `MostRecent`、`AutoLogin`、登录时间排序；设置页支持自动填充和“读取本地账号”多账号选择；`loginusers.vdf` 缺失时从 `userdata` 的 SteamID32 换算；补充账号解析、大小写时间戳和 userdata 回退测试。
4. 验证结果：79 项核心回归测试通过；本机实际 `loginusers.vdf` 读取到 6 个账号并自动填充最近登录账号；UI Automation 验证多账号弹窗、默认排序和选择其他账号后 SteamID64 正确更新；1.1.0 自包含发布与 Inno Setup 安装包编译成功；GitHub Actions 运行 [39363595234](https://github.com/yss161/Steam-BuildID-Manager/actions/runs/39363595234) 的 Core tests、Build portable、Build installer 全部成功，安装包完成静默安装/卸载冒烟测试。
5. 发布结果：创建 `v1.1.0` 标签，Release 同时附加便携版 zip 与安装版 exe。
