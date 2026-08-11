namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 数据库配置 Applied 事件通知
/// </summary>
public interface IDbConfigNotifier
{
    event EventHandler? Applied;
}
