# 第三方声明（THIRD-PARTY NOTICES）

NewToys 以 **MIT License** 开源（见 `LICENSE`）。本文件列出本项目**使用但未由本项目维护**的第三方组件及其许可协议，以满足 MIT 中「保留版权与许可声明」的要求。

## 一、NuGet 依赖（随构建链接进产物）

| 组件 | 版本 | 许可 | 项目主页 |
|---|---|---|---|
| Avalonia | 12.1.4 | MIT | https://github.com/AvaloniaUI/Avalonia |
| Avalonia.Desktop | 12.1.4 | MIT | 同上 |
| Avalonia.Themes.Fluent | 12.1.4 | MIT | 同上 |
| Avalonia.Fonts.Inter | 12.1.4 | MIT（内嵌 Inter 字体为 **SIL OFL 1.1**） | https://github.com/rsms/inter |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | https://github.com/CommunityToolkit/dotnet |
| Microsoft.Data.Sqlite | 9.0.0 | MIT（内含 SQLite，SQLite 为 **Public Domain**） | https://github.com/dotnet/efcore |
| Microsoft.Extensions.\*（DI / Configuration / Logging / Options） | 9.0.0 | MIT | https://github.com/dotnet/runtime |
| AngleSharp | 1.3.0 | MIT | https://github.com/AngleSharp/AngleSharp |
| Jint | 3.1.2 | **BSD 2-Clause** | https://github.com/sebastienros/jint |

> 版本集中管理在 `src/Directory.Packages.props`，改动只改这一处。

## 二、运行时平台

| 组件 | 许可 | 说明 |
|---|---|---|
| .NET 10 SDK / Runtime | MIT | 默认以**框架依赖**方式发布（目标机需装 .NET 10） |

## 三、外部程序（运行时以子进程调用，⛔ 不链接、不随包分发）

这些程序**不由本项目分发**，仅在用户本机已安装时被 NewToys 调用；其许可由各自项目提供，与 NewToys 的 MIT 授权互不影响。

| 程序 | 用途 | 许可 |
|---|---|---|
| `ffmpeg` / `ffprobe` | 视频信息读取、剪辑与转码 | LGPL-2.1+ 或 GPL-3.0+（取决于所用构建） |
| Windows Package Manager（`winget`） | 软件包的搜索 / 安装 / 升级 / 卸载 | MIT |
| Windows PowerShell 5.1 / PowerShell 7（`pwsh`） | 脚本与命令执行 | MIT |
| Python | 脚本执行 | PSF License |
| 系统 Shell（`cmd.exe` / `explorer` / `ms-settings:`） | 打开系统设置与文件位置 | Microsoft 软件许可条款 |

> ⚠️ 用户自行选择安装含 GPL 组件的 ffmpeg 构建时，**该构建仍受其自身许可约束**；NewToys 通过子进程调用它，不形成衍生作品关系。

## 四、SIL Open Font License（OFL 1.1）简要义务

`Avalonia.Fonts.Inter` 内嵌的 Inter 字体受 SIL OFL 1.1 约束：

- 可自由使用、研究、修改、再分发；
- **保留字体附带的版权声明**；
- 销售字体本体（单独售卖字体文件）受限制；
- ⛔ 保留字体名称（保留字体）在修改版中需改名。

完整文本见 https://scripts.sil.org/OFL

## 五、本项目资产的许可

- 全部源码（`src/`）、文档与协作规范（`skill/`）、构建脚本（`scripts/`）均以 **MIT** 提供，见 `LICENSE`。
- 若未来引入更严格的协议组件，本文件是唯一登记处，须同步更新。
