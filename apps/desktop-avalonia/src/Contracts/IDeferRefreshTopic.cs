namespace PacToolkits.Desktop.Avalonia.Contracts;

/// <summary>
/// 页面可声明某 watermark topic 本次延后刷新（编辑中/详情态避免抢写）
/// </summary>
public interface IDeferRefreshTopic
{
    bool DeferRefreshTopic(string? topic);
}

public interface IInventoryRefreshPage : IDeferRefreshTopic;

public interface IDrugIndexRefreshPage : IDeferRefreshTopic;
