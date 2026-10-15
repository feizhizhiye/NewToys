using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using NewToys.VideoTools.ViewModels;

namespace NewToys.VideoTools.Views;

/// <summary>
/// 「视频工具 → 视频预览」页（v3.12 由「命令生成」页更名，View / VM 类名与文件名同步改为<code>VideoPreview*</code>；
/// 菜单 key 仍是 <c>video-cut</c>，它落 config\columnlayout.json 的页键，改名会丢用户偏好）：
/// 只负责**制定一个**任务（剪辑 / 转码 / 剪辑+转码），编好后「➡ 发送到批处理」。
/// <para>列表与批量运行都搬到了「批量处理」页，所以这里没有 ContextMenu 需要同步。
/// 详情卡结构与批量处理页**一致**（见《工程规范》§3.9）。</para>
/// <para>平台文件选择器（源文件 / 输出文件）在这一层调用 —— VM 不引用 Avalonia 的 UI 类型。</para>
/// </summary>
public partial class VideoPreviewView : UserControl
{
    public VideoPreviewView()
    {
        InitializeComponent();
        // 选源文件 / 输出文件都要走平台选择器 —— VM 不引用 Avalonia 的 UI 类型，所以在这一层选完交给 VM。
        // ⚠️ NT-054：原来卡片里那个内联「📂」按钮（PickOutputBtn）随页内卡一起删了 ——
        //    现在它是声明式卡的 Actions 项，命令由本页在 OnDataContextChanged 里注入（见下）。
    }

    /// <summary>
    /// NT-054：把「📂 选择输出文件」的真实实现（要 TopLevel / StorageProvider）注入给声明式卡的动作。
    /// 每次 DataContext 变化重挂一次，幂等 —— 命令内部只认最新 VM。
    /// </summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is VideoPreviewViewModel vm)
            vm.PickOutputCommand = new RelayCommand(() => _ = PickOutputFileAsync());
    }

    /// <summary>
    /// 🔴 **取件点必须在这里，不能只在 <see cref="OnDataContextChanged"/>**（需求 §4.8 硬规则 8）。
    /// <para>标签页**已存在**时从别的页跳过来，Shell 只是把缓存的 View 重新挂回内容区 ——
    /// <c>DataContext</c> 没变，<c>OnDataContextChanged</c> **不会再触发**，于是送过来的文件一直没人取，
    /// 表现就是「必须刷新页面才看到预览」。<c>OnAttachedToVisualTree</c> 每次进页面都会跑，取一次即清空，重复调安全。</para>
    /// <para>⚠️ 基类 <c>Visual.OnAttachedToVisualTree</c> 是 **protected**，这里必须写 <c>protected override</c>，
    /// 写成 <c>public override</c> → <b>CS0507「无法更改访问修饰符」</b>。</para>
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (DataContext is VideoPreviewViewModel vm)
            vm.ConsumePending();
    }

    /// <summary>📂 给当前任务挑输出文件（另存为）。</summary>
    private async System.Threading.Tasks.Task PickOutputFileAsync()
    {
        if (DataContext is not VideoPreviewViewModel vm) return;
        if (string.IsNullOrWhiteSpace(vm.SourcePath)) { vm.StatusText = "先点「📂 选择视频」挑一个文件。"; return; }

        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider is not { } sp) { vm.StatusText = "当前环境不支持文件选择器。"; return; }

            var suggested = string.IsNullOrWhiteSpace(vm.EditOutput)
                ? System.IO.Path.GetFileNameWithoutExtension(vm.SourcePath) + "_cut.mp4"
                : System.IO.Path.GetFileName(vm.EditOutput);

            var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "输出文件保存到",
                SuggestedFileName = suggested,
                DefaultExtension = ".mp4",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("MP4 视频") { Patterns = new[] { "*.mp4" } },
                    new FilePickerFileType("所有文件") { Patterns = new[] { "*" } }
                }
            });

            var path = file?.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(path))
            {
                vm.EditOutput = path;
                vm.StatusText = "已选择输出文件：" + path;
            }
        }
        catch (Exception ex)
        {
            vm.StatusText = "选择输出文件失败：" + ex.Message;
        }
    }
}
