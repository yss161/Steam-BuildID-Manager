# 推送记录

## 2026-10-27 23:20:00 +08:00

1. 推送人员：圆鲨鲨
2. 目标分支：main
3. 实现内容：版本升级到 1.6.0；SteamCMD 增加「一键安装/更新/删除/打开目录」，自动下载官方 zip 到应用目录并解压，支持双镜像与 90 秒超时回退；修复「以管理员身份重启」无效的问题（重启前释放单实例互斥锁，失败则重新取回）；安装包安装完成时自动 Unblock-File，便携版附带 Unblock.cmd，用于消除 SmartScreen 的文件来源标记。
4. 验证结果：135 项核心回归测试通过，其中包含用本机回环 HTTP 服务实测“下载→解压→删除 steamcmd.exe”完整链路；UI 验证四个新按钮状态正确（未安装时“更新/删除”置灰）；提权改动通过代码路径与日志验证。
5. 说明：彻底消除 SmartScreen 需要代码签名证书，程序无法绕过；本机 nuget.org 解析被拦截，本地使用华为云镜像还原，CI 仍走官方源。
## 2026-10-27 21:50:00 +08:00

1. 推送人员：圆鲨鲨
2. 目标分支：main
3. 实现内容：版本升级到 1.5.0；新增 SteamCMD 数据源与「数据来源」设置（优先 SteamCMD / 优先在线接口 / 仅在线接口 / 仅 SteamCMD），支持指定或自动检测 steamcmd.exe、一键测试；新增通用 VDF 解析器与 SteamCMD `app_info_print` 结果解析；数据源失败自动回退，并加入 5 分钟熔断，单个 App 无公开数据不会禁用来源。
4. 验证结果：129 项核心回归测试通过；真机下载 SteamCMD 实测——本机 TLS 被拦截导致自更新失败，应用按预期在 30 秒内超时并回退到在线接口，其余 App 因熔断直接走接口，整轮 5 个配置约 33 秒完成（修复前需 4 分钟以上）；回退、熔断、无数据三种路径均有用例覆盖。
5. 说明：本机 nuget.org 解析被拦截，本地改用华为云 NuGet 镜像还原；CI 仍使用官方源。
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
