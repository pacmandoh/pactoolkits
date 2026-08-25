using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PacToolkits.Desktop.Avalonia.Controls;
using PacToolkits.Desktop.Avalonia.Views.Pages;

namespace PacToolkits.Desktop.UiTests;

public sealed class PageDataShellTests
{
    [AvaloniaFact]
    public void Interaction_lock_covers_projected_content()
    {
        var app = global::Avalonia.Application.Current
            ?? throw new InvalidOperationException("Headless Application was not created.");
        var baseUri = new Uri("avares://PacToolkits.Desktop/");
        var pacStyles = new StyleInclude(baseUri)
        {
            Source = new Uri("avares://PacToolkits.Desktop/Styles/Components/PageDataShell.axaml", UriKind.Absolute)
        };
        app.Styles.Add(pacStyles);

        var page = new Dashboard();
        var window = new Window { Content = page };

        window.Show();
        try
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var shell = page.GetVisualDescendants().OfType<PageDataShell>().Single();
            shell.ApplyTemplate();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var contentHost = shell.GetVisualDescendants()
                .OfType<ContentPresenter>()
                .Single(presenter => presenter.Name == "ContentHost");
            var interactionLock = shell.GetVisualDescendants()
                .OfType<Border>()
                .Single(border => border.Name == "InteractionLock");

            Assert.Same(page.Content, shell);
            Assert.NotNull(shell.Content);
            Assert.Same(shell.Content, contentHost.Content);

            shell.IsInteractionEnabled = false;
            Dispatcher.UIThread.RunJobs();
            Assert.False(contentHost.IsEnabled);
            Assert.True(interactionLock.IsVisible);
            Assert.True(interactionLock.IsHitTestVisible);

            shell.IsInteractionEnabled = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(contentHost.IsEnabled);
            Assert.False(interactionLock.IsVisible);
        }
        finally
        {
            window.Close();
            app.Styles.Remove(pacStyles);
        }
    }
}
