using System;
using Avalonia;
using Avalonia.Controls;

namespace PacToolkits.Desktop.Avalonia.Controls;

/// <summary>
/// Instantiates <see cref="ContentControl.ContentTemplate"/> only after <see cref="IsActive"/> becomes true once.
/// </summary>
public class DeferredContentHost : ContentControl
{
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<DeferredContentHost, bool>(nameof(IsActive));

    public event EventHandler<Control>? ContentLoaded;

    private bool _loaded;

    static DeferredContentHost()
    {
        IsActiveProperty.Changed.AddClassHandler<DeferredContentHost>((host, _) => host.TryLoadContent());
        ContentTemplateProperty.Changed.AddClassHandler<DeferredContentHost>((host, _) => host.TryLoadContent());
    }

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        TryLoadContent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (Content is Control child)
        {
            child.DataContext = DataContext;
        }
    }

    private void TryLoadContent()
    {
        if (!IsActive || _loaded || ContentTemplate is null)
        {
            return;
        }

        if (ContentTemplate.Build(null) is not Control view)
        {
            return;
        }

        _loaded = true;
        view.DataContext = DataContext;
        Content = view;
        ContentLoaded?.Invoke(this, view);
    }
}
