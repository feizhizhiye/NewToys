namespace NewToys.Shared.Services
{
    public interface IAppConfigService
    {
        // 注：**主题不属于这里**。明暗模式 / 强调色统一由 IThemeService 管（theme.json），
        // 本服务只管与主题无关的应用配置，避免出现两份"当前是不是深色"的真相。
        string DefaultVideoFolder { get; set; }
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
        /// 默认 <c>%LocalAppData%\NewToys\data</c>，可用环境变量 <c>NEWTOYS_DATA_DIR</c> 覆盖（便携版/测试），
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

        void Save();
        void Load();
    }
}