using System;

namespace PacToolkits.Desktop.Avalonia.ViewModels;

public sealed class PageNavigationService
{
    public Action<Type>? NavigationRequested;

    public void Navigate<TPage>() where TPage : AppPageBase
        => NavigationRequested?.Invoke(typeof(TPage));
}
