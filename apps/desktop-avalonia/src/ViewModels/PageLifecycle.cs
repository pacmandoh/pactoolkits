using System.Threading;
using System.Threading.Tasks;

namespace PacToolkits.Desktop.Avalonia.ViewModels;

public interface IPageLifecycleAware
{
    Task OnPageActivatedAsync(CancellationToken ct = default);

    Task OnPageDeactivatedAsync(CancellationToken ct = default);

    ValueTask DisposePageAsync();
}
