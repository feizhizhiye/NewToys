using NewToys.Shared.Models;

namespace NewToys.Shared.Services
{
    public interface IAppConfigService
    {
        // 注：**主题不属于这里**。明暗模式 / 强调色统一由 IThemeService 管（theme.json），
        // 本服务只管与主题无关的应用配置，避免出现两份"当前是不是深色"的真相。
        // 注：v1.34 曾移除"视频库默认目录"（库常分散在多文件夹，写死默认目录会误导）；
        // 重新引入为**全局导入/导出默认目录**（VideoDbFolder）：只用于"导入/导出对话框打开时定位到它"，
        // 不回写扫描目录、不影响"每次手动选目录"的逻辑。
        string FfmpegPath { get; set; }

        /// <summary>
        /// 外部视频播放器路径（exe 全路径）。留空 = 用系统默认关联程序打开。
        /// 本应用不做内嵌播放（主窗口是分层窗口，托管播放器画面只会黑屏）。
        /// </summary>
        string PlayerPath { get; set; }

        /// <summary>
        /// 🔴 v3.64：**已下沉到「视频工具」模块设置**（<c>config\video-tools.json</c> 的 <c>maxConcurrency</c>，
        /// 唯一写入点 =「设置 → 视频工具设置」页）。这里**只为一次性迁移保留读取**（`VideoToolsSettingsService`
        /// 首次运行会搬过去），搬完即不再读写。
        /// </summary>
        int MaxConcurrency { get; set; }
        // GpuAcceleration 已删除（无消费方）。旧 app.json 里的该键由
        // System.Text.Json 默认忽略未知属性 ⇒ 反序列化容错，不报错。

        /// <summary>
        /// <c>app.json</c> 全路径（v3.65：落**应用根**，即 <c>profiles\</c> 的同级 ——
        /// 「运行环境」是应用级路由，⛔ 不随配置档切换；环境变量 <c>NEWTOYS_HOME</c> 可整体换根）。
        /// </summary>
        string ConfigFilePath { get; }

        /// <summary>
        /// <c>app.json</c> 由旧位置（<c>&lt;配置根&gt;\config\</c>）上提到**应用根**时留下的说明；
        /// <c>null</c> = 本机没发生过迁移。设置页 / 排障可展示（记录迁移来源与时间点）。
        /// </summary>
        string? LegacyMigrationNote { get; }

        /// <summary>
        /// 当前配置档的**数据根**（= <c>&lt;配置根&gt;\store</c>：各模块自有库 / 规则 / 流程 / 脚本实体）。
        /// <para>默认导入 / 导出对话框就定位到这里；<c>NEWTOYS_HOME</c> 可把整个应用根换掉（便携版 / 测试）。</para>
        /// </summary>
        string StoreDirectory { get; }

        /// <summary>
        /// 当前配置档的**缓存根**（= <c>&lt;配置根&gt;\cache</c>：缩略图 / 页面缓存 / 下载中间产物 / 库升级前快照）。
        /// <para>可重建 ⇒ 不进备份；只读展示用（「设置 → 路径配置」）。</para>
        /// </summary>
        string CacheDirectory { get; }

        /// <summary>是否启用**加密 DNS（DoH）**——只作用于本软件自己的网络请求，不动系统设置。</summary>
        bool EncryptedDnsEnabled { get; set; }

        /// <summary>DoH 服务商 id（见 <see cref="DohProviders"/>：dnspod / aliyun / cloudflare / google / custom）。</summary>
        string EncryptedDnsProviderId { get; set; }

        /// <summary>自定义 DoH 端点（provider 选 custom 时用）。</summary>
        string EncryptedDnsCustomUrl { get; set; }

        // ==================== 路径配置（「设置 → 路径配置」页，复杂输入 → 需点保存） ====================
        // ⚠️ 浏览器 1/2 的**程序路径与启动参数不在这里** —— 它们属于"一键按钮"的配置，
        // 与按钮名、网址清单一起放在 launch.json 的 LaunchButtonConfig 里
        // （见「设置 → 一键按钮」页）。理由：同一个按钮的字段必须同源，
        // 否则会出现"路径配置改了 exe、一键按钮还用旧 exe"这种双真相源问题（v1.19 收敛）。

        // ==================== 运行环境（脚本解释器 + 脚本目录） ====================
        // 脚本文件（.py / .ps1 / .bat）靠这几个路径跑起来；前三个留空 = 从 PATH 找对应解释器。
        // 🔴 判据 = 语义：解释器路径描述「本机怎么跑外部程序」⇒ 应用级、不随配置档切换（写入点 = 设置 → 路径与配置）；
        //    仅 ScriptFolder 留模块级（config\script-manager.json）。

        /// <summary>Python 解释器路径（exe 全路径，也可只写命令名）。留空 = 用 PATH 里的 python。</summary>
        string PythonPath { get; set; }

        /// <summary>PowerShell 7+（pwsh）路径。留空 = 用 PATH 里的 pwsh（找不到时 .ps1 退到 5.1）。</summary>
        string PwshPath { get; set; }

        /// <summary>Windows PowerShell 5.1（powershell.exe）路径。留空 = 用系统自带的 powershell.exe。</summary>
        string PowerShell51Path { get; set; }

        /// <summary>
        /// 7-Zip 命令行（<c>7z.exe</c>）路径。留空 = 自动探测（常见安装位 → PATH 里的 <c>7z</c>）。
        /// <para>唯一写入点 =「设置 → 运行环境」；备份中心的归档引擎读它（⛔ 不再自己存一份）。</para>
        /// </summary>
        string SevenZipPath { get; set; }

        /// <summary>
        /// 更新检查用的**纯文本版本文件地址**（「设置 → 通用 → 启动时检查更新」用它）。
        /// 留空 = 不检查（页面会如实显示「未配置更新地址」，不假装检查过）。
        /// 内容期望是形如 <c>1.20261010.00.10</c> 的版本号（任意文本里有这么一串即可）。
        /// </summary>
        string UpdateUrl { get; set; }

        /// <summary>
        /// ⚠️ 已定为**模块级**（v3.65）：脚本目录的唯一写入点 = 「设置 → 脚本管理器设置」
        /// （<c>config\script-manager.json</c> 的 <c>scriptsFolder</c>）。这里**只为一次性迁移保留读取**，
        /// ⛔ 不再有 UI 写入它；值为空时**不落盘**（v3.66 起 app.json 里不会再出现这个键）。
        /// </summary>
        string ScriptFolder { get; set; }

        /// <summary>
        /// 一键按钮 + 行为开关（v3.66：2026-10-18）：原 <c>config\launch.json</c> 已并入 <c>app.json</c>，
        /// 唯一写入点仍是「设置 → 启动与行为」页（经 <see cref="ILaunchConfigService"/> 转手，⛔ 别直接写这个属性）。
        /// <para>理由：同为**应用级行为**（开机自启 / 关窗到托盘 / 一键按钮），按配置档隔离没有意义；合并后少一个文件、少一处双写。</para>
        /// </summary>
        LaunchConfig Launch { get; set; }

        // ==================== 导入 / 导出默认目录（v3.15 全局 → 改由 store 目录推导） ====================
        // 🔴 `VideoDbFolder` 字段已**删除**（2026-10-19）：它把某个 profile 的绝对路径写进跨配置共享的 app.json，
        // 切配置后导入/导出会指向别人的目录 —— 属现存缺陷。现在一律用 <see cref="StoreDirectory"/>（
        // = 当前配置档的 <c>store\</c>）推导，既跟随配置档，也不再需要用户维护这个路径。

        void Save();
        void Load();
    }
}