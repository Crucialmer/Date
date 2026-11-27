# Date 📅

<p align="center">
  <img src="Assets/app.png" alt="Date Logo" width="160" height="160" style="border-radius: 36px; box-shadow: 0 10px 30px rgba(0,0,0,0.3);"/>
</p>

<p align="center">
  <strong>基于 macOS 26 Liquid Glass 设计美学的 Windows 11 任务栏日期、星期与时间显示极简绿色工具</strong>
</p>

<p align="center">
  <a href="https://github.com/Crucialmer/Date/releases"><img src="https://img.shields.io/github/v/release/Crucialmer/Date?color=007AFF&style=flat-square" alt="Release"></a>
  <a href="https://dotnet.microsoft.com/"><img src="https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet" alt=".NET 10"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-blue.svg?style=flat-square" alt="License"></a>
  <a href="#"><img src="https://img.shields.io/badge/Platform-Windows%2011-0078D4?style=flat-square&logo=windows11" alt="Platform"></a>
</p>

---

## 🌟 特性亮点

- **🍎 macOS 26 Liquid Glass 液态玻璃美学**：
  - 全曲面同心圆角悬浮胶囊（标准任务栏 / 小任务栏双密度自适应），各向异性高光折射边框与柔和顶光微层。
  - 指针悬停微光响应，不争夺窗口焦点（`WS_EX_NOACTIVATE`），不占用 Alt+Tab 切换列表。
  - 统一 Liquid Glass 风格的右键菜单（配 SF-Symbol 风格矢量图标）与右下角滑入式更新通知卡片。
- **📌 任务栏原生级吸附与置顶防消失**：
  - 自动吸附在 Windows 11 系统托盘左侧，严格上下光学居中，支持标准与小任务栏。
  - 免疫「显示桌面」与 `Win + D` 清屏，自动响应 DPI 缩放、分辨率变化、休眠唤醒与任务栏重启。
  - 零闲置开销按需 Z 序校验，静默运行接近 0% CPU 占用。
- **🌓 深浅色无感自适应**：
  - 实时监听 Windows 11 系统主题，智能切换 Obsidian 深色液态玻璃与 Crystal 浅色水晶玻璃，支持手动锁定深色/浅色。
- **🍃 单文件纯正绿色便携**：
  - 全部图标资源内嵌于单个 `Date.exe`，配置（`config.json`）严格保存在程序同目录；仅在手动勾选「开机自启动」时写入当前用户启动项，取消即刻清理。
- **🔄 一键自动更新**：
  - 启动后台静默检测与菜单手动检查，直连 GitHub Releases，支持实时进度下载与安全覆盖重启。

---

## 🖼️ 预览与图标设计

| macOS 26 应用图标 | 托盘状态栏图标 |
|:---:|:---:|
| <img src="Assets/app.png" width="128" /> | <img src="Assets/tray.png" width="48" /> |
| 液态流光连续曲率 Squircle | 高对比度像素对齐微型日历 |

---

## ⚙️ 配置文件 (`config.json`)

程序首次运行后会在自身目录下自动生成 `config.json`，可根据个人偏好进行修改：

```json
{
  "DateFormat": "yyyy年M月d日",
  "DayOfWeekFormat": "dddd",
  "ShowTime": true,
  "TimeFormat": "HH:mm",
  "ThemeMode": "Auto",
  "OffsetLeft": 0,
  "CheckUpdatesOnStartup": true,
  "AutoStart": false,
  "GitHubRepo": "Crucialmer/Date"
}
```

* **`DateFormat`**：日期格式（支持任意标准 .NET 日期格式化字符串，例如 `yyyy/MM/dd`、`M月d日`）。
* **`DayOfWeekFormat`**：星期格式（例如 `dddd` 为 `星期三`，`ddd` 为 `周三`）。
* **`ShowTime` / `TimeFormat`**：是否在星期右侧显示实时时间及时间格式（默认 `HH:mm`，等宽数字防抖动）。
* **`ThemeMode`**：外观模式，可选 `"Auto"`（跟随系统）、`"Dark"`（深色）、`"Light"`（浅色）。
* **`OffsetLeft`**：胶囊右边缘与托盘区域左侧边缘的像素间距。

---

## 🛠️ 编译与打包

### 前置要求
- Windows 11 (22H2 / 23H2 / 24H2 或更高版本)
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### 快速本地运行
```bash
git clone https://github.com/Crucialmer/Date.git
cd Date
dotnet run
```

### 发布单文件便携绿色版
```bash
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o ./publish
```
编译产物即在 `./publish/Date.exe`，单文件直接双击即可运行。

---

## 📄 开源许可证

本项目基于 [MIT License](LICENSE) 开源。
