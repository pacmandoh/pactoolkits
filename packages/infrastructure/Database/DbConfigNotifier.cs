using PacToolkits.Application.Abstractions;

namespace PacToolkits.Infrastructure.Database;

public sealed class DbConfigNotifier : IDbConfigNotifier
{
    public DbConfigNotifier(IDbConfigService dbConfig)
    {
        dbConfig.Applied += OnDbConfigApplied;
    }

    public event EventHandler? Applied;

    private void OnDbConfigApplied(object? sender, EventArgs e)
        => Applied?.Invoke(this, e);
}
