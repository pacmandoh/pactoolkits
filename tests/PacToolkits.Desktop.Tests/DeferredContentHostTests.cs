using Avalonia.Controls;
using Avalonia.Controls.Templates;
using PacToolkits.Desktop.Avalonia.Controls;

namespace PacToolkits.Desktop.Tests;

public sealed class DeferredContentHostTests
{
    [Fact]
    public void Activating_once_materializes_template_content_only_once()
    {
        var host = new DeferredContentHost();
        var builds = 0;
        host.ContentTemplate = new FuncDataTemplate<object?>((_, _) =>
        {
            builds++;
            return new TextBlock { Text = "loaded" };
        });

        host.IsActive = true;

        Assert.Equal(1, builds);
        var firstContent = Assert.IsType<TextBlock>(host.Content);
        Assert.Equal("loaded", firstContent.Text);

        host.IsActive = false;
        host.IsActive = true;

        Assert.Equal(1, builds);
        Assert.Same(firstContent, host.Content);
    }

    [Fact]
    public void ContentLoaded_fires_when_template_is_materialized()
    {
        var host = new DeferredContentHost();
        Control? loaded = null;
        host.ContentLoaded += (_, control) => loaded = control;
        host.ContentTemplate = new FuncDataTemplate<object?>((_, _) => new Border());

        host.IsActive = true;

        Assert.NotNull(loaded);
        Assert.Same(host.Content, loaded);
    }

    [Fact]
    public void Inactive_host_does_not_materialize_template()
    {
        var host = new DeferredContentHost
        {
            ContentTemplate = new FuncDataTemplate<object?>((_, _) => new TextBlock())
        };

        Assert.Null(host.Content);
    }

    [Fact]
    public void Setting_template_after_activation_materializes_content()
    {
        var host = new DeferredContentHost
        {
            IsActive = true,
            ContentTemplate = new FuncDataTemplate<object?>((_, _) => new TextBlock { Text = "late" })
        };

        Assert.IsType<TextBlock>(host.Content);
        Assert.Equal("late", Assert.IsType<TextBlock>(host.Content).Text);
    }

    [Fact]
    public void DataContext_change_propagates_to_materialized_child()
    {
        var host = new DeferredContentHost
        {
            ContentTemplate = new FuncDataTemplate<object?>((_, _) => new Border()),
            IsActive = true,
            DataContext = "first"
        };

        var child = Assert.IsType<Border>(host.Content);
        Assert.Equal("first", child.DataContext);

        host.DataContext = "second";
        Assert.Equal("second", child.DataContext);
    }
}
