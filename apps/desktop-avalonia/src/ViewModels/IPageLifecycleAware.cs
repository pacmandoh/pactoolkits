using System.Threading;
using System.Threading.Tasks;

namespace PacToolkits.Desktop.Avalonia.ViewModels;

/// <summary>
/// 页面激活/停用与释放钩子；由 MainWindow 在导航切换时调用
/// </summary>
public interface IPageLifecycleAware
{
    Task OnPageActivatedAsync(CancellationToken ct = default);

    Task OnPageDeactivatedAsync(CancellationToken ct = default);

    ValueTask DisposePageAsync();
}
