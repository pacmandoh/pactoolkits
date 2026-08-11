using Avalonia.Controls;
using global::Avalonia.Controls.Templates;
using PacToolkits.Desktop.Avalonia.ViewModels;

namespace PacToolkits.Desktop.Avalonia.Navigation;

/// <summary>按照 ViewModel 类型解析 Avalonia DataTemplate 对应的 View</summary>
public class ViewLocator(AppViews views) : IDataTemplate
{
    public Control Build(object? param)
    {
        if (param is Control c)
        {
            return c;
        }

        if (param is null)
        {
            return CreateText("Data is null.");
        }

        if (param is AppPageBase page && views.TryCreateView(page, out var pageView))
        {
            pageView.DataContext = page;
            return pageView;
        }

        if (views.TryCreateView(param, out var view))
        {
            view.DataContext = param;
            return view;
        }

        return CreateText($"No View For {param.GetType().Name}.");
    }

    public bool Match(object? data) => data is CommunityToolkit.Mvvm.ComponentModel.ObservableObject;

    private static TextBlock CreateText(string text) => new TextBlock { Text = text };
}
