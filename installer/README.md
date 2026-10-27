# 安装版构建

安装包使用 [Inno Setup 6](https://jrsoftware.org/isinfo.php) 制作，需要管理员权限，安装到 `Program Files`。

## 本地构建

1. 先发布自包含应用：

   ```powershell
   dotnet publish src/SteamBuildIdManager.App/SteamBuildIdManager.App.csproj `
     -c Release -r win-x64 --self-contained true `
     -p:Platform=x64 -p:WindowsAppSDKSelfContained=true `
     -p:PublishReadyToRun=false -o publish
   ```

2. 安装 Inno Setup 6（可用 `winget install JRSoftware.InnoSetup`）。

3. 编译安装包：

   ```powershell
   & "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" installer\SteamBuildIDManager.iss
   ```

产物：`artifacts\installer\Steam-BuildID-Manager-Setup-1.5.0.exe`。

## 安装行为

1. 安装程序启动后自动请求管理员权限（`PrivilegesRequired=admin`），UAC 提示中选择“是”才能继续。
2. 默认安装到 `C:\Program Files\SteamBuildIDManager`，开始菜单快捷方式写入所有用户，桌面快捷方式默认不勾选。
3. 安装程序会自动关闭正在运行的 `SteamBuildIDManager.exe`（如果存在）。
4. 卸载需要管理员权限，入口位于 Windows“设置 → 应用”，不会删除用户设置和 ACF 备份。

> Inno Setup 生成的 `Setup.exe` 自身清单是 `asInvoker`，实际提权由安装程序运行时调用 `runas` 完成，这是 Inno Setup 的正常设计。

## 说明

`ChineseSimplified.isl` 来自 Inno Setup 官方仓库的简体中文翻译文件，版权归其译者所有，仅用于安装界面本地化。
