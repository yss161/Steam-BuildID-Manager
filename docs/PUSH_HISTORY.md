# 推送记录

## 2026-10-20 10:40:00 +08:00

1. 推送人员：圆鲨鲨
2. 目标分支：main
3. 实现内容：版本升级到 1.0.1；新增 Inno Setup 安装版、简体中文安装界面、按用户安装、开始菜单与可选桌面快捷方式、安装前关闭旧版本；新增 GitHub Actions 安装包构建、静默安装/卸载冒烟测试和双产物 Release。
4. 验证结果：73 项核心回归测试通过；自包含 1.0.1 发布成功；本地 ISCC 编译安装包成功（约 60.6 MB），安装包可正常启动；本机沙箱对开始菜单与 HKCU 卸载项的 ACL 限制导致静默安装无法在本机完整完成，改由 GitHub Actions runner 执行安装/卸载冒烟测试。
5. 发布计划：推送后创建 `v1.0.1` 标签，Release 同时附加便携版 zip 与安装版 exe。

## 2026-10-20 09:21:37 +08:00

1. 推送人员：圆鲨鲨
2. 目标分支：main
3. 实现内容：将 SteamCN-GameLauncher 精简为 Steam-only 的 Steam BuildID Manager；新增 BuildID 更新检测、单个/批量一键更新、ACF 自动备份、配置新建与重建、Steam 路径探测、Steam 运行中警告和陈旧 ACF 拒绝覆盖；新增 GitHub Actions 云编译与 Release 附件。
4. 验证结果：73 项核心回归测试全部通过；本地与 GitHub Actions 的 win-x64 自包含产物均能启动并显示主窗口；Actions 运行 [39352987036](https://github.com/yss161/Steam-BuildID-Manager/actions/runs/39352987036) 的 Core tests 与 Build win-x64 package 均成功。
5. 发布结果：创建 `v1.0.0` 标签，由同一工作流生成 GitHub Release 并附加 `Steam-BuildID-Manager-win-x64.zip`。
6. 已知限制：公开 BuildID 依赖 `api.steamcmd.net`；只更新 BuildID，不修复 Depot 内容；CI 使用 `windows-latest`，首次运行需下载 Windows App SDK 包。