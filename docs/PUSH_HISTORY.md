# 推送记录

## 2026-11-03 20:50:00 +08:00

1. 推送人员：圆鲨鲨
2. 目标分支：main
3. 实现内容：版本升级到 1.8.0；把 Valve 官方 SteamCMD 引导程序内置到安装包，安装到 `{app}\steamcmd`；首次检测时自动部署到可写的 `%LocalAppData%\SteamBuildIDManager\steamcmd`（SteamCMD 需自更新，Program Files 不可写），新增 `DeployBundledCopy` 与 `SteamCmdLocator.ResolveOrDeploy`，部署为幂等刷新且失败不中断检测；CI 在打包前抓取 steamcmd.zip 并校验 payload 存在。
4. 验证结果：156 项核心回归测试通过（新增 6 项，覆盖内置副本部署、整目录复制、幂等刷新、删除与跳过）；本地 ISCC 编译确认 `installer\steamcmd\steamcmd.exe` 被打进安装包；真机验证内置副本部署到临时可写目录成功，版本 02.02.95.35；UI 验证设置页三种状态文案与四个按钮状态正确。
5. 说明：内置的仅是 Valve 引导程序，SteamCMD 首次运行仍需联网自更新；SteamCMD 版权归 Valve，已附 NOTICE；本机 nuget.org 被拦截，本地用华为云镜像还原，CI 走官方源。

## 2026-11-02 17:10:00 +08:00

1. 推送人员：圆鲨鲨
2. 目标分支：main
3. 实现内容：版本升级到 1.7.0；整合原项目 v3.2.1 的数据获取改造——新增本机 Steam 缓存读取（appinfo.vdf 格式 39/40/41）与 SteamKit2 3.4.0 匿名直连 PICS，默认取值顺序改为「本机缓存 → Steam 直连 → SteamCMD → api.steamcmd.net」；新增 SteamUpdateInfoResolver 多源仲裁（完整快照、剔除冲突、取最高 BuildID、不跨源拼接）与 SteamManifestUpdateGuard 版本证据校验；新增“来源滞后”状态，不再把落后于 Steam 已知版本的数据源显示为可一键修复。
4. 验证结果：150 项核心回归测试通过（新增 15 项，覆盖守卫、仲裁、缓存格式拒绝与冲突诊断）；真机实测本机缓存解析 AppID 4706890 得到 BuildID 26373441 与 Manifest 4548954592484496523，与 Steam 一致；SteamKit2 匿名直连实测成功返回 4706890 与 730 的公开产品信息；Core 与 App 构建通过，发布目录包含 SteamKit2.dll 及 LGPL 许可文件。
5. 说明：SteamKit2 为社区协议库（LGPL-2.1），以未修改 NuGet 包引用并附带许可文件；本机 nuget.org 解析被拦截，本地使用华为云镜像还原，CI 仍走官方源。

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
