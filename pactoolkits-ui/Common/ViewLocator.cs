using Avalonia.Controls;
using Avalonia.Controls.Templates;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using pactoolkits_ui.ViewModels;

namespace pactoolkits_ui.Common;

public class ViewLocator(AppViews views) : IDataTemplate
{
    private readonly Dictionary<object, Control> _controlCache = [];

    public Control Build(object? param)
    {
        if (param is Control c) return c;
        if (param is null) return CreateText("Data is null.");

        if (param is AppPageBase)
        {
            if (_controlCache.TryGetValue(param, out var control))
            {
                AppLog.Info("ViewLocator", "view.cache.hit", "Reused cached view", new
                {
                    cacheCount = _controlCache.Count,
                    vm = param.GetType().Name
                });
                return control;
            }

            if (views.TryCreateView(param, out var view))
            {
                view.DataContext = param;
                _controlCache.Add(param, view);
                AppLog.Info("ViewLocator", "view.cache.add", "Cached new view", new
                {
                    cacheCount = _controlCache.Count,
                    vm = param.GetType().Name
                });
                return view;
            }
        }
        else
        {
            if (views.TryCreateView(param, out var view))
            {
                view.DataContext = param;
                AppLog.Info("ViewLocator", "view.create.new", "Created non-page view", new
                {
                    cacheCount = _controlCache.Count,
                    vm = param.GetType().Name
                });
                return view;
            }
        }

        return CreateText($"No View For {param.GetType().Name}.");
    }

    public bool Match(object? data) => data is ObservableObject;

    private static TextBlock CreateText(string text) => new TextBlock { Text = text };
}
