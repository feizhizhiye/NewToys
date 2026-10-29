using Microsoft.Extensions.DependencyInjection;
using NewToys.BackupCenter.Services;
using NewToys.BackupCenter.Services.Mail;
using NewToys.BackupCenter.ViewModels;
using NewToys.BackupCenter.Views;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.BackupCenter;

/// <summary>
/// 备份中心模块（一级菜单「备份中心」，组键 <c>backup</c>）：
/// 备份任务 / 备份包 / 设置三页，服务全部注册在本模块内。
/// </summary>
public sealed class BackupCenterModule : IModule
{
    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<BackupDb>();
        services.AddSingleton<BackupSettingsService>();
        services.AddSingleton<BackupPlanStore>();
        services.AddSingleton<ArchiveCatalog>();
        services.AddSingleton<BackupRunLog>();
        services.AddSingleton<BackupTodoStore>();
        services.AddSingleton<VaultService>();
        services.AddSingleton<SevenZipLocator>();
        services.AddSingleton<ArchiveService>();
        services.AddSingleton<MailJobStore>();
        services.AddSingleton<MailIndexStore>();
        services.AddSingleton<BackupMailService>();
        services.AddSingleton<ImapMailService>();
        services.AddSingleton<OutboxService>();

        services.AddTransient<BackupPlansView>();
        services.AddTransient<BackupPlansViewModel>();
        services.AddTransient<BackupArchivesView>();
        services.AddTransient<BackupArchivesViewModel>();
        services.AddTransient<BackupCalendarView>();
        services.AddTransient<BackupCalendarViewModel>();
        services.AddTransient<BackupMailView>();
        services.AddTransient<BackupMailViewModel>();
        services.AddTransient<SettingsBackupView>();
        services.AddTransient<SettingsBackupViewModel>();
    }

    public void Initialize(IServiceProvider services)
    {
        var menu = services.GetRequiredService<IMenuService>();
        menu.RegisterModule("backup", "备份中心", "IconBackup", 60);

        menu.RegisterFunction<BackupPlansView>("backup", "backup-plans", "备份任务", "🗄", 1,
            "选目录 → 7z 加密归档 → 按份数轮转");
        menu.RegisterFunction<BackupArchivesView>("backup", "backup-archives", "备份包", "📦", 2,
            "本地归档包清单、校验值与还原");
        menu.RegisterFunction<BackupCalendarView>("backup", "backup-calendar", "备份日历", "📅", 3,
            "按天看备份记录，写下哪天要备份");
        menu.RegisterFunction<BackupMailView>("backup", "backup-mail", "备份邮件", "📧", 4,
            "邮箱里的邮件与备份分卷，搜索 / 下载 / 还原");
        menu.RegisterFunction<SettingsBackupView>("backup", "settings-backup", "备份中心设置", "🔐", 100,
            "主密码密码库、保留份数与目录、邮件投递");
    }
}
