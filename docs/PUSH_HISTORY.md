# 推送记录

## 2026-10-20 09:35:00 +08:00

1. 推送人员：圆鲨鲨
2. 目标分支：main
3. 实现内容：将 SteamCN-GameLauncher 精简为 Steam-only 的 Steam BuildID Manager；新增 BuildID 更新检测、单个/批量一键更新、ACF 自动备份、配置新建与重建、Steam 路径探测、Steam 运行中警告和陈旧 ACF 拒绝覆盖；新增 GitHub Actions 云编译与 Release 附件。
4. 验证结果：73 项核心回归测试全部通过；本地 win-x64 自包含发布成功，程序窗口可正常启动；GitHub Actions 运行结果待首次推送后确认。
5. 已知限制：公开 BuildID 依赖 `api.steamcmd.net`；只更新 BuildID，不修复 Depot 内容；CI 使用 `windows-latest`，首次运行需下载 Windows App SDK 包。