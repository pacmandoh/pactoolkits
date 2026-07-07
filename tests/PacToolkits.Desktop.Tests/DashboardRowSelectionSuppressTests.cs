namespace PacToolkits.Desktop.Tests;

public sealed class DashboardRowSelectionSuppressTests
{
    [Fact]
    public void ApplyDrugSpecFilter_keeps_row_selection_suppressed_through_reload()
    {
        var source = ReadRepoFile("apps/desktop-avalonia/src/ViewModels/Pages/Dashboard.cs");

        Assert.Contains("BeginRowSelectionSuppress()", source, StringComparison.Ordinal);
        Assert.Contains("await ReloadNow();", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_suppressRowSelectionAction", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Overview_selection_handler_stays_synced_through_async_apply()
    {
        var source = ReadRepoFile("apps/desktop-avalonia/src/Views/Pages/DashboardView.axaml.cs");

        Assert.Contains("_syncingSelection = true;", source, StringComparison.Ordinal);
        Assert.Contains("await vm.OpenTxnAsync", source, StringComparison.Ordinal);
        Assert.Contains("_syncingSelection = false;", source, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine(root, relativePath));
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "PacToolkits.sln")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName ?? string.Empty;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
