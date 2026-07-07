using PacToolkits.Desktop.Avalonia.ViewModels.Dialogs;
using ShadUI;

namespace PacToolkits.Desktop.Tests;

public sealed class SensitiveUnlockTests
{
    [Fact]
    public void ClearSensitiveState_clears_password_and_errors()
    {
        var vm = new SensitiveUnlock(new DialogManager());
        vm.Initialize("title", "hint", static _ => "wrong password");
        vm.Password = "secret";

        vm.ClearSensitiveState();

        Assert.Equal(string.Empty, vm.Password);
        Assert.Null(vm.PasswordError);
    }
}
