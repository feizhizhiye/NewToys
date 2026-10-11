namespace NewToys.Shared.Services
{
    public interface IAppConfigService
    {
        // 注：**主题不属于这里**。明暗模式 / 强调色统一由 IThemeService 管（theme.json），
        // 本服务只管与主题无关的应用配置，避免出现两份"当前是不是深色"的真相。
        //
        // 注：v1.34 曾移除"视频库默认目录"（库常分散在多文件夹，写死默认目录会误导）；
        // v3.15 重新引入为**全局导入/导出默认目录**（VideoDbFolder）：只用于"导入/导出对话框打开时定位到它"，
        // 不回写扫描目录、不影响"每次手动选目录"的逻辑。
        string FfmpegPath { get; set; }

        /// <summary>
        /// 外部视频播放器路径（exe 全路径）。留空 = 用系统默认关联程序打开。
        /// 本应用不做内嵌播放（主窗口是分层窗口，托管播放器画面只会黑屏）。
        /// </summary>
        string PlayerPath { get; set; }

        int MaxConcurrency { get; set; }
        bool GpuAcceleration { get; set; }

        /// <summary>配置文件完整路径（<c>%LocalAppData%\NewToys\config.json</c>）。</summary>
        string ConfigFilePath { get; }

        /// <summary>
        /// 应用数据根目录（媒体库 library.json、缩略图、各模块自己的数据都放这里）。
        /// 默认 <c>%LocalAppData%\NewToys\data</c>；环境变量 <c>NEWTOYS_HOME</c> 可把整个应用根（config/data/logs）一起换掉（便携版/测试），
        /// 用户目录不可写时兜底到程序目录的 <c>Data\</c>。放在用户目录是为了**跨 Debug/Release、跨清理输出目录都不丢数据**。
        /// </summary>
        string DataDirectory { get; }

        /// <summary>是否启用**加密 DNS（DoH）**——只作用于本软件自己的网络请求，不动系统设置。</summary>
        bool EncryptedDnsEnabled { get; set; }

        /// <summary>DoH 服务商 id（见 <see cref="DohProviders"/>：dnspod / aliyun / cloudflare / google / custom）。</summary>
        string EncryptedDnsProviderId { get; set; }

        /// <summary>自定义 DoH 端点（provider 选 custom 时用）。</summary>
        string EncryptedDnsCustomUrl { get; set; }

        // ==================== 路径配置（「设置 → 路径配置」页，复杂输入 → 需点保存） ====================
        //
        // ⚠️ 浏览器 1/2 的**程序路径与启动参数不在这里** —— 它们属于"一键按钮"的配置，
        // 与按钮名、网址清单一起放在 launch.json 的 LaunchButtonConfig 里
        // （见「设置 → 一键按钮」页）。理由：同一个按钮的字段必须同源，
        // 否则会出现"路径配置改了 exe、一键按钮还用旧 exe"这种双真相源问题（v1.19 收敛）。

        // ==================== 运行环境（脚本解释器 + 脚本目录，v1.33） ====================
        // 用途：「电脑工具 → 设置与脚本」页里的**脚本文件**（.py / .ps1 / .bat）靠这几个路径跑起来。
        // 前三个**留空 = 用系统默认**（从 PATH 里找 python / pwsh / powershell）；
        // 不填也能跑，只是机器上得真有那个解释器。

        /// <summary>Python 解释器路径（exe 全路径，也可只写命令名）。留空 = 用 PATH 里的 python。</summary>
        string PythonPath { get; set; }

        /// <summary>PowerShell 7+（pwsh）路径。留空 = 用 PATH 里的 pwsh（找不到时 .ps1 退到 5.1）。</summary>
        string PwshPath { get; set; }

        /// <summary>Windows PowerShell 5.1（powershell.exe）路径。留空 = 用系统自带的 powershell.exe。</summary>
        string PowerShell51Path { get; set; }

        /// <summary>
        /// 更新检查用的**纯文本版本文件地址**（「设置 → 通用 → 启动时检查更新」用它）。
        /// 留空 = 不检查（页面会如实显示「未配置更新地址」，不假装检查过）。
        /// 内容期望是形如 <c>1.20261010.00.10</c> 的版本号（任意文本里有这么一串即可）。
        /// </summary>
        string UpdateUrl { get; set; }

        /// <summary>脚本目录（「设置与脚本」页引用的脚本文件都放这里；留空 = <see cref="DefaultScriptFolder"/>）。</summary>
        string ScriptFolder { get; set; }

        /// <summary>脚本目录的默认值：<c>&lt;应用根&gt;\scripts</c>（即 config / data 的同级）。</summary>
        string DefaultScriptFolder { get; }

        // ==================== 导入 / 导出默认目录（v3.15，全局） ====================
        //
        // ⚠️ 字段名沿用 <c>VideoDbFolder</c> 仅为向后兼容（早期只管视频库 .db）；v3.15 升为**全局导入/导出默认目录**：
        // 视频库 .db 与批量任务 .json 的导入/导出文件选择器打开时直接定位到它
        // （StorageProvider.TryGetFolderFromPathAsync + SuggestedStartLocation）。留空 = 用系统默认。

        /// <summary>
        /// 导入 / 导出默认目录：视频库 <c>.db</c> 与批量任务 <c>.json</c> 的导入/导出文件选择器打开时定位到它。
        /// 留空 = 用系统默认（系统文档 / 视频目录）。后续新增的导入/导出对话框也应复用它。
        /// </summary>
        string VideoDbFolder { get; set; }

        void Save();
        void Load();
    }
}