using System.Collections.ObjectModel;
using PacToolkits.Desktop.Avalonia.Common;

namespace PacToolkits.Desktop.Tests;

public sealed class ObservableCollectionExtensionsTests
{
    [Fact]
    public void ReplaceAll_does_not_issue_reset_when_sizes_match()
    {
        var target = new ObservableCollection<string> { "a", "b" };
        var resets = 0;
        target.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
                resets++;
            }
        };

        target.ReplaceAll(["x", "y"]);

        Assert.Equal(0, resets);
        Assert.Equal(["x", "y"], target);
    }

    [Fact]
    public void ReplaceAll_trims_extra_items_without_reset()
    {
        var target = new ObservableCollection<int> { 1, 2, 3 };
        var resets = 0;
        target.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
                resets++;
            }
        };

        target.ReplaceAll([10]);

        Assert.Equal(0, resets);
        Assert.Equal([10], target);
    }

    [Fact]
    public void ReplaceAll_appends_new_items_without_reset()
    {
        var target = new ObservableCollection<string> { "keep" };
        var resets = 0;
        target.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
                resets++;
            }
        };

        target.ReplaceAll(["keep", "added"]);

        Assert.Equal(0, resets);
        Assert.Equal(["keep", "added"], target);
    }

    [Fact]
    public void ReplaceAll_skips_replace_when_items_are_equal()
    {
        var target = new ObservableCollection<string> { "same" };
        var changes = 0;
        target.CollectionChanged += (_, _) => changes++;

        target.ReplaceAll(["same"]);

        Assert.Equal(0, changes);
    }

    [Fact]
    public void SyncContentsInPlace_preserves_shared_item_identity_without_collection_changes()
    {
        var first = new MutableRow(1);
        var second = new MutableRow(2);
        var target = new ObservableCollection<MutableRow> { first, second };
        var changes = 0;
        target.CollectionChanged += (_, _) => changes++;

        target.SyncContentsInPlace(
            [new MutableRow(11), new MutableRow(12)],
            static (existing, incoming) => existing.Value = incoming.Value);

        Assert.Equal(0, changes);
        Assert.Same(first, target[0]);
        Assert.Same(second, target[1]);
        Assert.Equal([11, 12], target.Select(static row => row.Value));
    }

    private sealed class MutableRow(int value)
    {
        public int Value { get; set; } = value;
    }
}
