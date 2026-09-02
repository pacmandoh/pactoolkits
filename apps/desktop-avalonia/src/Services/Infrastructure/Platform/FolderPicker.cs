using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Platform;

/// <summary>通过当前主窗口选择本地文件夹</summary>
public interface IFolderPickerService
{
    void Bind(Func<IStorageProvider?> providerFactory);
    Task<string?> PickFolderAsync(CancellationToken ct = default);
}

public sealed class FolderPickerService : IFolderPickerService
{
    private Func<IStorageProvider?>? _providerFactory;

    public void Bind(Func<IStorageProvider?> providerFactory)
        => _providerFactory = providerFactory ?? throw new ArgumentNullException(nameof(providerFactory));

    public async Task<string?> PickFolderAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var provider = _providerFactory?.Invoke();
        if (provider is null)
        {
            return null;
        }

        var folders = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择导出文件夹",
            AllowMultiple = false
        }).ConfigureAwait(true);

        if (folders.Count == 0)
        {
            return null;
        }

        return folders[0].TryGetLocalPath();
    }
}
