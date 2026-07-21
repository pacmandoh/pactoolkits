using System;
using System.Collections.Generic;
using System.Linq;
using global::Avalonia.Controls;
using PacToolkits.Desktop.Avalonia.ViewModels;
using PacToolkits.Desktop.Avalonia.ViewModels.Dialogs;

namespace PacToolkits.Desktop.Avalonia.Common;

/// <summary>按类型解析 View 实例</summary>
public sealed class AppViews
{
    private readonly Dictionary<string, Type> _viewTypesByName;

    public AppViews()
    {
        var assemblies = new[]
        {
            typeof(AppViews).Assembly,
            typeof(Views.MainWindow).Assembly,
        }.Distinct().ToArray();

        _viewTypesByName = assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => typeof(Control).IsAssignableFrom(t))
            .GroupBy(t => t.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
    }

    public bool TryCreateView(object viewModel, out Control view)
    {
        view = null!;

        var vmName = viewModel.GetType().Name;
        var viewTypeName = ResolveViewTypeName(viewModel, vmName);
        if (viewTypeName is null)
        {
            return false;
        }

        if (!_viewTypesByName.TryGetValue(viewTypeName, out var viewType))
        {
            AppLog.Warn("AppViews", "view.resolve.fail", "No view type mapped for viewmodel", null, new
            {
                viewTypeName,
                vm = viewModel.GetType().FullName
            });
            return false;
        }

        view = (Control)Activator.CreateInstance(viewType)!;
        return true;
    }

    private static string? ResolveViewTypeName(object viewModel, string vmName)
    {
        if (viewModel is AppPageBase)
        {
            return vmName;
        }

        if (viewModel is FormBase)
        {
            return vmName + "View";
        }

        if (vmName.EndsWith("ViewModel", StringComparison.Ordinal))
        {
            return vmName[..^"ViewModel".Length] + "View";
        }

        return null;
    }
}
