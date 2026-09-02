using PacToolkits.Application.Abstractions;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Security;

namespace PacToolkits.Desktop.Tests.ViewModels.Settings;

public sealed class SettingsCursorAdvanceAuthorizationTests
{
    [Fact]
    public async Task Denied_unlock_does_not_open_confirmation()
    {
        var calls = new List<string>();
        var unlock = new FakeUnlock(false, calls);

        var authorized = await PacToolkits.Desktop.Avalonia.ViewModels.Pages.Settings
            .AuthorizeCursorAdvanceAsync(
                unlock,
                () =>
                {
                    calls.Add("confirm");
                    return Task.FromResult(true);
                },
                TestContext.Current.CancellationToken);

        Assert.False(authorized);
        Assert.Equal(["unlock"], calls);
    }

    [Fact]
    public async Task Granted_unlock_opens_confirmation_after_authorization()
    {
        var calls = new List<string>();
        var unlock = new FakeUnlock(true, calls);

        var authorized = await PacToolkits.Desktop.Avalonia.ViewModels.Pages.Settings
            .AuthorizeCursorAdvanceAsync(
                unlock,
                () =>
                {
                    calls.Add("confirm");
                    return Task.FromResult(true);
                },
                TestContext.Current.CancellationToken);

        Assert.True(authorized);
        Assert.Equal(["unlock", "confirm"], calls);
    }

    private sealed class FakeUnlock(bool granted, List<string> calls) : ISensitiveUnlockService
    {
        public event Action<string>? StateChanged
        {
            add { }
            remove { }
        }

        public UnlockScopeSnapshot GetSnapshot(string scopeKey)
            => new(false, DateTimeOffset.MinValue, 0, DateTimeOffset.MinValue);

        public void Refresh(string scopeKey)
        {
        }

        public void Lock(string scopeKey)
        {
        }

        public Task<bool> RequestUnlockAsync(SensitiveOpRequest request, CancellationToken ct = default)
            => Task.FromResult(granted);

        public Task<bool> RequireUnlockAsync(
            string scopeKey,
            string scene,
            string promptTitle,
            string promptHint,
            CancellationToken ct = default)
        {
            calls.Add("unlock");
            Assert.Equal(UnlockScopes.SharedOps, scopeKey);
            return Task.FromResult(granted);
        }
    }
}
