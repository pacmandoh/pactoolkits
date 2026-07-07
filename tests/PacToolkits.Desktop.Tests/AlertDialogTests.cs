using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Dialogs;
using ShadUI;

namespace PacToolkits.Desktop.Tests;

public sealed class AlertDialogTests
{
    [Fact]
    public void Ack_uses_outline_style()
        => Assert.Equal(DialogButtonStyle.Outline, AlertLayout.Style(AlertRole.Ack));

    [Fact]
    public void Cancel_and_dismiss_use_ghost_style()
    {
        Assert.Equal(DialogButtonStyle.Ghost, AlertLayout.Style(AlertRole.Cancel));
        Assert.Equal(DialogButtonStyle.Ghost, AlertLayout.Style(AlertRole.Dismiss));
    }

    [Fact]
    public void Affirm_uses_primary_and_danger_uses_destructive()
    {
        Assert.Equal(DialogButtonStyle.Primary, AlertLayout.Style(AlertRole.Affirm));
        Assert.Equal(DialogButtonStyle.Destructive, AlertLayout.Style(AlertRole.Danger));
    }
}
