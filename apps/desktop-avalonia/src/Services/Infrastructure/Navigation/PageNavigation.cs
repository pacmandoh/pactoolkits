using System;
using PacToolkits.Desktop.Avalonia.ViewModels;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Navigation;

/// <summary>工作区页面导航请求中转</summary>
public sealed class PageNavigationService
{
    public Action<Type>? NavigationRequested;

    public void Navigate<TPage>() where TPage : AppPageBase
        => NavigationRequested?.Invoke(typeof(TPage));
}
