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
        vm.Initialize("title", "hint", static _ => Task.FromResult<string?>("wrong password"));
        vm.Password = "secret";

        vm.ClearSensitiveState();

        Assert.Equal(string.Empty, vm.Password);
        Assert.Null(vm.PasswordError);
    }

    [Fact]
    public async Task Initialize_preserves_verify_callback()
    {
        var verified = false;
        var vm = new SensitiveUnlock(new DialogManager());
        vm.Initialize("title", "hint", password =>
        {
            verified = true;
            Assert.Equal("secret", password);
            return Task.FromResult<string?>(null);
        });
        vm.Password = "secret";

        await InvokeSubmitAsync(vm);

        Assert.True(verified);
    }

    [Fact]
    public async Task Submit_without_verify_does_not_close_successfully()
    {
        var vm = new SensitiveUnlock(new DialogManager());
        vm.Initialize("title", "hint", static _ => Task.FromResult<string?>(null));
        vm.ClearSensitiveState();
        vm.Password = "secret";

        await InvokeSubmitAsync(vm);

        Assert.Equal("验证未就绪，请关闭后重试", vm.PasswordError);
    }

    [Fact]
    public async Task Submit_empty_password_asks_for_unlock_password()
    {
        var vm = new SensitiveUnlock(new DialogManager());
        vm.Initialize("title", "hint", static _ => Task.FromResult<string?>(null));

        await InvokeSubmitAsync(vm);

        Assert.Equal("请输入敏感操作密码", vm.PasswordError);
    }

    private static Task InvokeSubmitAsync(SensitiveUnlock vm)
    {
        var result = vm.GetType()
            .GetMethod("Submit", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(vm, null);
        return result as Task ?? Task.CompletedTask;
    }
}
