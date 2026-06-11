using System;

namespace pactoolkits_ui.ViewModels;

public sealed class PageNavigationService
{
    public Action<Type>? NavigationRequested;

    public void Navigate<TPage>() where TPage : AppPageBase
        => NavigationRequested?.Invoke(typeof(TPage));
}
