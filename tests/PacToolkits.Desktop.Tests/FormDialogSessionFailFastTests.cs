using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Dialogs;
using ShadUI;

namespace PacToolkits.Desktop.Tests;

[Collection("Avalonia")]
public sealed class FormDialogSessionFailFastTests
{
    [Fact]
    public void EnsureOpenControlRegistered_throws_when_show_leaves_no_open_control()
    {
        var manager = new DialogManager();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            FormDialogSession.EnsureOpenControlRegistered(manager, typeof(StubContext)));

        Assert.Contains("did not register an open control after Show.", ex.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(StubContext), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShowAsync_throws_when_dialog_type_is_not_registered()
    {
        var manager = new DialogManager();
        var vm = new StubContext();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            FormDialogSession.ShowAsync(
                manager,
                vm,
                prepare: null,
                onSuccess: _ => true,
                onCancel: () => false));

        Assert.Contains("is not registered.", ex.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(StubContext), ex.Message, StringComparison.Ordinal);
    }

    private sealed class StubContext;
}
