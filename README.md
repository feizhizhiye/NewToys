# NewToys

**Windows 桌面工具集** —— 把命令行、系统设置、winget、ffmpeg 与自建脚本，收进一个模块化窗口里点一下就用。

![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg) ![Platform](https://img.shields.io/badge/Platform-Windows-blue.svg) ![.NET](https://img.shields.io/badge/.NET-10-512BD4) ![UI](https://img.shields.io/badge/UI-Avalonia%2012-8B44D9)

个人高强度使用工具集。**Avalonia 12 + .NET 10** 版本。

> 想了解它**是什么 / 怎么用**（与技术栈无关的说明）：见 `skill/docs/NewToys-软件说明书.md`。

> 技术栈：**Avalonia 12 + .NET 10**（不是 WPF，XAML 语法有差异）。
> 全部约定见 `skill/docs/` 下 **4 份**文档（需求规格 / 工程规范 / 项目状态与路线图 / AI协作手册）；**任务型 → 规范章节速查表**见 `AGENTS.md`。

<img width="1104" height="721" alt="图片" src="https://github.com/user-attachments/assets/7ccb3081-05e5-4f3d-b03d-52c45163829c" />

## 目录地图

```
NewToys/
├── src/                  纯净源码区（可整包转发）：8 个项目 + slnx + 两个 props
├── skill/                跨 AI 知识库（自封装）
│   ├── docs/             4 份权威文档（宪法）+ backlog/ 任务卡
│   ├── references/       项目约束 / 通用方法论 / 技术栈硬伤清单
│   └── scripts/          4 项静态检查 + accept.ps1（一键验收）
├── scripts/              构建 / 环境 / 发布 / 打包脚本
├── artifacts/            产物区（忽略）：bin\ release\ env\ context\
└── production/           publish.ps1 的输出目标（忽略）
```

## 常用命令（PowerShell 7 / pwsh）

> ⚠️ **解决方案文件只有 `src\NewToys.slnx` 一个**。
> 根目录那份 `NewToys.slnx` 是 props 收进 `src/` 之前的旧副本（没登记两个 props），目前碰巧也能编，但**不再维护**，建议删除以免混淆。

| 想干什么 | 命令 |
|---|---|
| 编译 Debug | `pwsh -File .\scripts\build.ps1` |
| 编译 Release | `pwsh -File .\scripts\build.ps1 -Configuration Release` |
| 全量 Release | `pwsh -File .\scripts\release.ps1` |
| 指定模块 Release | `pwsh -File .\scripts\release-module.ps1 -Module VideoTools` |
| 清过包 / 改过版本后先还原 | `pwsh -File .\scripts\build.ps1 -Restore` |
| 指定平台构建（框架依赖） | `pwsh -File .\scripts\build.ps1 -Configuration Release -Runtime linux-x64` |
| 指定平台构建（自包含） | `pwsh -File .\scripts\build.ps1 -Configuration Release -Runtime osx-x64 -SelfContained` |
| 构建并启动 | `pwsh -File .\scripts\run.ps1 [-Configuration Debug] [-Env test]` |
| 直接运行（已构建，缺失提醒先构建） | `pwsh -File .\scripts\run-NewToys.ps1 [-Configuration Debug] [-Env test]` |
| 查看 / 切换 测试·生产环境 | `pwsh -File .\scripts\env.ps1 [test\|prod]` |
| 发布到目录 | `pwsh -File .\scripts\publish.ps1 [-Target D:\NewToys]` |
| 清理产物 | `pwsh -File .\scripts\clean.ps1` |
| 打包源码快照（喂 AI / 转发） | `pwsh -File .\scripts\pack.ps1` |

等价原始命令：`dotnet build src\NewToys.slnx -c Debug`。

> 默认目标平台为 **win-x64（框架依赖，运行机需装 .NET 10）**；`src/Directory.Build.props` 中锁定。跨平台构建见上表 `-Runtime`。

> **终端窗口行为**：脚本成功即自关、失败才停留（fail-loud），直接双击 `.ps1` 即可，无需启动器或快捷方式。详见 `README-构建说明.md`「终端窗口行为」。

## 产物路径

| 产物 | 路径 |
|---|---|
| Debug 主程序 | `artifacts\bin\Shell\Debug\net10.0\NewToys.exe` |
| Release 输出根 | `artifacts\release\`（`/p:NewToysReleaseDir=<dir>\` 可覆盖） |
| Release 模块 DLL | `artifacts\release\Modules\Module.*.dll`（**MSBuild 直出，零拷贝**） |
| 用户数据 | `%LocalAppData%\NewToys\{config,data,logs}`（`NEWTOYS_HOME` 可整体换根） |

## 版本号

**编译时自动生成**（`src/Directory.Build.props`），格式 `1.<yyyyMMdd>.<HH>.<mm 取整到十位>`，
「设置 · 关于」页显示。**不要手写版本号。**

## 当前状态

- ✅ Debug / Release **均 0 Error / 0 Warning**
- **版本记录走 git commit**（`<type>: 简述`），⛔ 不写 md 变更日志；完整约定详见 `AGENTS.md`
- 详细基线与工作项见 `skill/docs/NewToys-项目状态与路线图.md`

## 四个功能模块

| 模块 | 干什么 |
|---|---|
| 脚本管理器 | 收集脚本 / 命令 / 采集器 / winget 包，并把脚本的结构化输出做成信息面板 |
| 网络工具 | HTTP 测试、书签管理、网络 Ping（含 DNS 切换）、网页抓取、Hosts 管理 |
| 视频工具 | 视频查看器 + ffmpeg 剪辑 / 转码命令的制定与批量执行 |
| 媒体资产 | 本地文件资产库：扫描索引 + 标签 / 分类 / 集合 / 任务 / 回收站 |

## 许可

| 文件 | 内容 |
|---|---|
| `LICENSE` | **MIT**（本项目源码） |
| `THIRD-PARTY-NOTICES.md` | 第三方依赖与运行时外部程序的许可清单 |
| `CONTRIBUTING.md` | 贡献流程与本项目的四条铁律 |
| `SECURITY.md` | 漏洞上报与本项目的威胁模型 |
