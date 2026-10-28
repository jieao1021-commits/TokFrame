# 抖窗 DouChuang（TokFrame.exe）

轻量简洁的第三方抖音网页版浏览工具。双击即进入抖音网页版，无边框、无地址栏、无标签页。

> ⚠️ **声明**：本项目为个人开发的第三方工具，与字节跳动 / 抖音官方无任何关联，仅供学习交流使用。
> 使用者需自行遵守抖音平台的服务条款。

## 功能

- 无边框、无标签、无按钮，打开即是抖音网页版（https://www.douyin.com）
- 黑色顶栏（Edge 风格）：整条可拖动，右侧四个按钮 —— 置顶 / 最小化 / 最大化 / 关闭
- 支持 Windows 贴靠分屏（拖到屏幕边缘 / 顶部 / 四角）
- 单文件免安装（WebView2 依赖已内嵌），约 1MB
- 单实例运行、自动记忆窗口大小与位置
- 快捷键：`F11` 全屏、`Ctrl+W` 关闭、`Ctrl+Shift+K` 清缓存（保留登录）、`Ctrl+Shift+L` 清除全部数据

## 下载与使用

1. 前往 [Releases](../../releases) 下载 `TokFrame.exe`
2. 双击运行
3. 首次运行若弹出 Windows SmartScreen 提示（“Windows 已保护你的电脑”），
   点击「更多信息」→「仍要运行」即可。程序未做数字签名，属正常提示。
4. （可选）校验文件完整性：

   ```powershell
   certutil -hashfile TokFrame.exe SHA256
   ```

## 系统要求

| 系统 | 支持 |
| --- | --- |
| Windows 11 | ✅ 直接使用（系统自带 WebView2 运行时） |
| Windows 10 1803+ | ✅ 可用；若提示缺少 WebView2，安装微软官方「WebView2 Evergreen Runtime」一次即可 |
| Windows 7 / 8 | ❌ 不支持（WebView2 运行时官方已停止支持） |

另需 .NET Framework 4.8（Windows 10 1903+ 与 Windows 11 系统自带）。

## 构建

需要 .NET Framework 4.8 编译器（Windows 自带 csc.exe）：

```powershell
.\build.ps1
```

输出到 `app\TokFrame.exe`。手动构建等价命令：

```powershell
csc /target:winexe /platform:anycpu /optimize+ /win32icon:build\icon.ico /win32manifest:src\app.manifest /out:app\TokFrame.exe /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:lib\Microsoft.Web.WebView2.Core.dll /r:lib\Microsoft.Web.WebView2.WinForms.dll /resource:lib\Microsoft.Web.WebView2.Core.dll,DouyinBrowser.WebView2.Core.dll /resource:lib\Microsoft.Web.WebView2.WinForms.dll,DouyinBrowser.WebView2.WinForms.dll /resource:lib\WebView2Loader.dll,DouyinBrowser.WebView2Loader.dll src\browser.cs
```

## 隐私

- 不收集任何数据
- 登录状态、缓存仅保存在本机：`%LOCALAPPDATA%\DouyinBrowser\`
- 不联网访问任何自建服务器

## 许可

[MIT License](LICENSE)。内嵌的 Microsoft Edge WebView2 组件版权归 Microsoft 所有（见 [NOTICE](NOTICE.txt)）。

## 免责声明

本软件按「现状」提供，不保证无缺陷或适用于任何特定用途；
使用本软件产生的一切后果由使用者自行承担。
