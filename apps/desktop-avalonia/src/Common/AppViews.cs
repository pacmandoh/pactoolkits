using System;
using System.Collections.Generic;
using System.Linq;
using global::Avalonia.Controls;

namespace PacToolkits.Desktop.Avalonia.Common;

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

        if (!vmName.EndsWith("ViewModel", StringComparison.Ordinal))
        {
            return false;
        }

        var viewTypeName = vmName[..^"ViewModel".Length] + "View";

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
}
