# 推送记录

## 2026-10-20 09:21:37 +08:00

1. 推送人员：圆鲨鲨
2. 目标分支：main
3. 实现内容：将 SteamCN-GameLauncher 精简为 Steam-only 的 Steam BuildID Manager；新增 BuildID 更新检测、单个/批量一键更新、ACF 自动备份、配置新建与重建、Steam 路径探测、Steam 运行中警告和陈旧 ACF 拒绝覆盖；新增 GitHub Actions 云编译与 Release 附件。
4. 验证结果：73 项核心回归测试全部通过；本地与 GitHub Actions 的 win-x64 自包含产物均能启动并显示主窗口；Actions 运行 [39352987036](https://github.com/yss161/Steam-BuildID-Manager/actions/runs/39352987036) 的 Core tests 与 Build win-x64 package 均成功。
5. 发布结果：创建 `v1.0.0` 标签，由同一工作流生成 GitHub Release 并附加 `Steam-BuildID-Manager-win-x64.zip`。
6. 已知限制：公开 BuildID 依赖 `api.steamcmd.net`；只更新 BuildID，不修复 Depot 内容；CI 使用 `windows-latest`，首次运行需下载 Windows App SDK 包。