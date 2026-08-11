using PacToolkits.Application.Abstractions;

namespace PacToolkits.Infrastructure.Database;

/// <summary>
/// 数据库配置已应用事件的转发器
///
/// 将 <c>IDbConfigService.Applied</c> 转换为应用层可订阅的数据库配置通知
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
