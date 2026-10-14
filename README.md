# NewToys

个人高强度使用工具集。**Avalonia 12 + .NET 10** 版本。

> 技术栈：**Avalonia 12 + .NET 10**（不是 WPF，XAML 语法有差异）。
> 全部约定见 `src/docs/` 下 **4 份**文档（需求规格 / 工程规范 / 项目状态与路线图 / AI协作手册）；**任务型 → 规范章节速查表**见 `AGENTS.md`。

## 目录地图

```
NewToys/
├── src/                  纯净源码区（可整包转发）：7 个项目 + slnx + 两个 props + docs
├── scripts/              构建 / 环境 / 发布 / 打包脚本
├── artifacts/            产物区（忽略）：bin\ release\ env\ context\
├── production/           publish.ps1 的输出目标（忽略）
└── src/docs/             4 份规范文档（宪法）
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
- 详细基线与工作项见 `src/docs/NewToys-项目状态与路线图.md`
