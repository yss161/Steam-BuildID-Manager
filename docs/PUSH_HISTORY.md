# 推送记录

## 2026-10-20 18:00:00 +08:00

1. 推送人员：圆鲨鲨
2. 目标分支：main
3. 实现内容：版本升级到 1.3.0；新增“运行日志”面板与 `app.log` 文件日志，覆盖扫描、检测、更新、配置写入和网络请求；Steam 占位文件名改为随 AppID 自动获取，优先取 appinfo 启动 EXE 文件名，回退到安装目录名；「获取游戏信息」新增占位文件名展示。
4. 验证结果：102 项核心回归测试通过；本机 UI Automation 验证日志面板实时显示 7 条扫描/检测/获取记录、`HYP.exe` 在 AppID 4162040 下自动填入占位文件名字段；1.3.0 自包含发布与 Inno Setup 安装包编译成功。
5. 说明：占位文件名必须与 Steam appinfo 记录一致，否则 Steam 无法启动配置。

## 2026-10-20 17:25:00 +08:00

1. 推送人员：圆鲨鲨
2. 目标分支：main
3. 实现内容：版本升级到 1.2.0；安装包改为 `PrivilegesRequired=admin` 全机安装并自动申请管理员权限；新增跟随系统/浅色/深色主题、DWM 沉浸式深色标题栏与 Mica 背景；新增本地背景图片与 10%–100% 不透明度滑块；新增“以管理员身份启动”开关与一键提权重启；新增“获取游戏信息”对话框，展示显示名称、安装目录名、BuildID、主 DepotID、主 Manifest、启动 EXE 和全部 Depot，并支持复制启动命令。
4. 验证结果：94 项核心回归测试通过；本机验证主题切换即时生效并持久化、背景图片与遮罩正常渲染、深色标题栏颜色正确、真实 AppID 4706890 与 4162040 均正确返回主 Depot 与主 Manifest、启动命令成功写入剪贴板；1.2.0 自包含发布与 Inno Setup 安装包编译成功。
5. 已知限制：本机沙箱无法响应 UAC 提示，安装与提权的完整交互由 GitHub Actions runner 冒烟测试覆盖。

## 2026-10-20 11:20:00 +08:00

1. 推送人员：圆鲨鲨
2. 目标分支：main
3. 实现内容：版本升级到 1.1.0；新增本地 Steam 账号读取，解析 `config\loginusers.vdf` 并按 `MostRecent`、`AutoLogin`、登录时间排序；设置页支持自动填充和“读取本地账号”多账号选择；`loginusers.vdf` 缺失时从 `userdata` 的 SteamID32 换算；补充账号解析、大小写时间戳和 userdata 回退测试。
4. 验证结果：79 项核心回归测试通过；本机实际 `loginusers.vdf` 读取到 6 个账号并自动填充最近登录账号；UI Automation 验证多账号弹窗、默认排序和选择其他账号后 SteamID64 正确更新；1.1.0 自包含发布与 Inno Setup 安装包编译成功；GitHub Actions 运行 [39363595234](https://github.com/yss161/Steam-BuildID-Manager/actions/runs/39363595234) 的 Core tests、Build portable、Build installer 全部成功，安装包完成静默安装/卸载冒烟测试。
5. 发布结果：创建 `v1.1.0` 标签，Release 同时附加便携版 zip 与安装版 exe。

## 2026-10-20 10:40:00 +08:00

1. 推送人员：圆鲨鲨
2. 目标分支：main
3. 实现内容：版本升级到 1.0.1；新增 Inno Setup 安装版、简体中文安装界面、按用户安装、开始菜单与可选桌面快捷方式、安装前关闭旧版本；新增 GitHub Actions 安装包构建、静默安装/卸载冒烟测试和双产物 Release。
4. 验证结果：73 项核心回归测试通过；自包含 1.0.1 发布成功；本地 ISCC 编译安装包成功（约 60.6 MB）；GitHub Actions 运行 [39360218903](https://github.com/yss161/Steam-BuildID-Manager/actions/runs/39360218903) 的 Core tests、Build portable win-x64 package、Build installer 全部成功，安装包在 CI 中完成静默安装、EXE/PRI 检查和静默卸载；本机沙箱对开始菜单与 HKCU 卸载项的 ACL 限制不影响真实用户安装。
5. 发布结果：创建 `v1.0.1` 标签，Release 同时附加便携版 zip 与安装版 exe。

## 2026-10-20 09:21:37 +08:00

1. 推送人员：圆鲨鲨
2. 目标分支：main
3. 实现内容：将 SteamCN-GameLauncher 精简为 Steam-only 的 Steam BuildID Manager；新增 BuildID 更新检测、单个/批量一键更新、ACF 自动备份、配置新建与重建、Steam 路径探测、Steam 运行中警告和陈旧 ACF 拒绝覆盖；新增 GitHub Actions 云编译与 Release 附件。
4. 验证结果：73 项核心回归测试全部通过；本地与 GitHub Actions 的 win-x64 自包含产物均能启动并显示主窗口；Actions 运行 [39352987036](https://github.com/yss161/Steam-BuildID-Manager/actions/runs/39352987036) 的 Core tests 与 Build win-x64 package 均成功。
5. 发布结果：创建 `v1.0.0` 标签，由同一工作流生成 GitHub Release 并附加 `Steam-BuildID-Manager-win-x64.zip`。
6. 已知限制：公开 BuildID 依赖 `api.steamcmd.net`；只更新 BuildID，不修复 Depot 内容；CI 使用 `windows-latest`，首次运行需下载 Windows App SDK 包。