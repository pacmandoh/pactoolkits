namespace PacToolkits.Desktop.Avalonia.Contracts;

public interface IDeferRefreshTopic
{
    bool DeferRefreshTopic(string? topic);
}

public interface IInventoryRefreshPage : IDeferRefreshTopic;

public interface IDrugIndexRefreshPage : IDeferRefreshTopic;
