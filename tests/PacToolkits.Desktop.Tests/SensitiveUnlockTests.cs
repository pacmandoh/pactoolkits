using System.Reflection;
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

    [Fact]
    public void Initialize_preserves_verify_callback()
    {
        var verified = false;
        var vm = new SensitiveUnlock(new DialogManager());
        vm.Initialize("title", "hint", password =>
        {
            verified = true;
            Assert.Equal("secret", password);
            return null;
        });
        vm.Password = "secret";

        InvokeSubmit(vm);

        Assert.True(verified);
    }

    [Fact]
    public void Submit_without_verify_does_not_close_successfully()
    {
        var vm = new SensitiveUnlock(new DialogManager());
        vm.Initialize("title", "hint", static _ => null);
        vm.ClearSensitiveState();
        vm.Password = "secret";

        InvokeSubmit(vm);

        Assert.Equal("验证未就绪，请关闭后重试", vm.PasswordError);
    }

    private static void InvokeSubmit(SensitiveUnlock vm)
        => vm.GetType().GetMethod("Submit", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null);
}
