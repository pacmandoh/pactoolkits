using PacToolkits.Application.Services;

namespace PacToolkits.Desktop.Tests;

public sealed class DatabaseAccessGuardTests
{
    [Fact]
    public void Block_prevents_access_until_cleared()
    {
        var guard = new DatabaseAccessGuard();

        guard.Block("数据库版本过高");

        Assert.True(guard.IsBlocked);
        var exception = Assert.Throws<InvalidOperationException>(guard.ThrowIfBlocked);
        Assert.Contains("数据库版本过高", exception.Message, StringComparison.Ordinal);

        guard.Clear();

        Assert.False(guard.IsBlocked);
        guard.ThrowIfBlocked();
    }
}
