# 推送记录

## 2026-11-11 16:40:00 +08:00

1. 推送人员：圆鲨鲨
2. 目标分支：main
3. 实现内容：版本升级到 1.9.2；定位到「错误 5: 拒绝访问」是 Inno 在运行任何脚本代码之前解压自身到 `%TEMP%\is-XXXX.tmp` 导致，`.iss` 内无法拦截；新增 `Start-Setup.cmd` + `Start-Setup.ps1` 启动器，按四个候选目录依次验证可写性后设置 `TEMP`/`TMP` 再启动安装程序，保留完整向导界面；删除无效的 `InitializeSetup` 预检；启动器随便携版 zip 分发并作为独立 Release 附件；CI 增加幂等守卫与「拒绝写入 TEMP 后仍能通过启动器真实安装」的回归测试。
4. 验证结果：156 项核心回归测试通过；启动器本地实测两条路径——正常环境选中 `%LocalAppData%\SteamBuildIDManager\InstallTemp` 并正确传递给子进程、`TEMP` 指向无效路径时自动回退；CI 通过 `icacls` 拒绝写入后经启动器完成真实静默安装与卸载；本地沙箱使用虚拟 SID，`icacls` 拒绝对其无效，因此拒绝写入场景改由 CI 覆盖。
5. 说明：启动器只切换临时目录，不改系统安全设置；保留 `PrivilegesRequiredOverridesAllowed=dialog`，默认仍为免提权用户级安装。

## 2026-11-11 15:30:00 +08:00

1. 推送人员：圆鲨鲨
2. 目标分支：main
3. 实现内容：版本升级到 1.9.1；新增 `installer/sign.ps1` 代码签名管线（支持 PFX/Base64 PFX/证书指纹，SHA256 + RFC3161 时间戳，签名后校验，自动跳过已签名的 `steamcmd.exe`）；CI 增加可选签名步骤，由 `SIGNING_*` 密钥控制，未配置时不影响构建；README 新增「SmartScreen 提示说明」，如实说明提示成因、处理方式与 SignPath/EV/OV 方案差异。
4. 验证结果：156 项核心回归测试通过；签名脚本三条路径已实测——无证书时告警并正常退出、已签名文件正确跳过、配置证书但缺少 signtool 时明确报错而非静默产出未签名包；本机未安装 Windows SDK 的 signtool，实际签名动作由 CI 在 windows-latest 上覆盖。
5. 说明：SmartScreen 提示出现在安装包启动之前，程序内部无法消除，只有代码签名有效；本版本不改变程序功能。

## 2026-11-03 21:45:00 +08:00

1. 推送人员：圆鲨鲨
2. 目标分支：main
3. 实现内容：版本升级到 1.9.0；安装器改为与原项目一致的用户级安装（`PrivilegesRequired=lowest`、装到 `%LocalAppData%\Programs`、免 UAC），修复双击安装包报“临时目录没有写入权限”（全机安装时 `{tmp}` 指向 `C:\Windows\Temp`）；新增 `InitializeSetup` 临时目录预检并给出可操作提示；向导改为 Windows 11 现代风格 + 简体中文；新增 `AppMutex`、自动关闭旧版本、过期 WinUI 语言目录清理；CI 冒烟测试改为断言 HKCU 用户级卸载项。
4. 验证结果：156 项核心回归测试通过；ISCC 编译成功且 `publish\steamcmd\steamcmd.exe` 已打入安装包；本地沙箱无法运行安装器（Setup 初始化即被环境限制，与代码无关），安装/卸载行为改由 CI 静默安装冒烟测试验证，覆盖 exe、PRI、SteamCMD payload 与 HKCU 注册项。
5. 说明：用户级安装后 `{app}` 位于用户可写范围，但内置 SteamCMD 仍按原设计复制到 `%LocalAppData%\SteamBuildIDManager\steamcmd` 运行；本机 nuget.org 被拦截，本地用华为云镜像还原，CI 走官方源。

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
