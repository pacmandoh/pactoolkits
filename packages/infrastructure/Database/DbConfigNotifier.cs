using PacToolkits.Application.Abstractions;

namespace PacToolkits.Infrastructure.Database;

/// <summary>
/// 数据库配置已应用事件的转发器
///
/// 负责：将 <c>IDbConfigService.Applied</c> 桥接到 <c>IDbConfigNotifier</c>
/// 不持有配置内容本身
/// </summary>
public sealed class DbConfigNotifier : IDbConfigNotifier
{
    public DbConfigNotifier(IDbConfigService dbConfig)
    {
        dbConfig.Applied += OnDbConfigApplied;
    }

    public event EventHandler? Applied;

    private void OnDbConfigApplied(object? sender, EventArgs e)
        => Applied?.Invoke(this, e);
}
