using CommunityToolkit.Mvvm.ComponentModel;

namespace NewToys.PcTools.Models;

/// <summary>
/// 「电脑工具 → 系统信息」列表的一行：一条 **可读的系统事实**（分类 + 项目 + 值）。
/// <para>
/// 之所以做成列表而不是写死一排标签：信息项的数量是**动态的**（磁盘有几个盘就几行、
/// 网卡有几张就几行、还能顺带把「设置 → 路径配置」里的默认路径一起列出来），
/// 写死的网格排版根本装不下。
/// </para>
/// </summary>
public partial class PcInfoEntry : ObservableObject
{
    /// <summary>分类（操作系统 / CPU / 内存 / 磁盘 / 网络 / 运行时 / 应用路径）。列表可按它排序、筛选。</summary>
    [ObservableProperty] private string _category = "";

    /// <summary>项目名（如「处理器」「C:\ 可用空间」）。</summary>
    [ObservableProperty] private string _name = "";

    /// <summary>值。</summary>
    [ObservableProperty] private string _value = "";

    /// <summary>补充说明（值看不懂时看这里；可空）。</summary>
    [ObservableProperty] private string _note = "";

    public PcInfoEntry() { }

    public PcInfoEntry(string category, string name, string value, string note = "")
    {
        Category = category;
        Name = name;
        Value = value;
        Note = note;
    }

    /// <summary>复制 / 导出用的一行文本。</summary>
    public string ToLine() => $"{Category}\t{Name}\t{Value}";
}
