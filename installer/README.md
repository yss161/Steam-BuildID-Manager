# 安装版构建

安装包使用 [Inno Setup 6](https://jrsoftware.org/isinfo.php) 制作，按用户安装，不请求管理员权限。

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

产物：`artifacts\installer\Steam-BuildID-Manager-Setup-1.0.1.exe`。

## 安装行为

1. 安装到 `%LocalAppData%\Programs\SteamBuildIDManager`，无需管理员权限。
2. 创建开始菜单快捷方式；桌面快捷方式默认不勾选。
3. 安装程序会自动关闭正在运行的 `SteamBuildIDManager.exe`（如果存在）。
4. 卸载入口位于 Windows“设置 → 应用”，不会删除用户设置和 ACF 备份。

## 说明

`ChineseSimplified.isl` 来自 Inno Setup 官方仓库的简体中文翻译文件，版权归其译者所有，仅用于安装界面本地化。