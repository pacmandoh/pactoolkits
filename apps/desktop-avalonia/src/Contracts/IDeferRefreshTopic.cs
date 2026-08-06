namespace PacToolkits.Desktop.Avalonia.Contracts;

/// <summary>
/// 允许页面在编辑或查看详情期间延后处理指定数据变更主题
/// </summary>
public interface IDeferRefreshTopic
{
    bool DeferRefreshTopic(string? topic);
}

public interface IInventoryRefreshPage : IDeferRefreshTopic;

public interface IDrugIndexRefreshPage : IDeferRefreshTopic;

public interface IMsfxRefreshPage : IDeferRefreshTopic;
