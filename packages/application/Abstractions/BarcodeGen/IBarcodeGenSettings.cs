namespace PacToolkits.Application.Abstractions;

/// <summary>条码生成配置仅持久化在 Desktop 本地</summary>
public interface IBarcodeGenSettingsStore
{
    BarcodeGenOptions Load();
    Task SaveAsync(BarcodeGenOptions options, CancellationToken ct = default);
}

/// <summary>向 Desktop 页面发布当前条码生成配置</summary>
public interface IBarcodeGenSettingsService
{
    BarcodeGenOptions Current { get; }
    event Action? Changed;
    void Apply(BarcodeGenOptions options);
    Task SaveAsync(BarcodeGenOptions options, CancellationToken ct = default);
}
