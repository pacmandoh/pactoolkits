using System;
using PacToolkits.Desktop.Avalonia.ViewModels;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure;

public sealed class PageNavigationService
{
    public Action<Type>? NavigationRequested;

    public void Navigate<TPage>() where TPage : AppPageBase
        => NavigationRequested?.Invoke(typeof(TPage));
}
