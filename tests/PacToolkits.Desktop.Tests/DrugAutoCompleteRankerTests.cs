using PacToolkits.Application.TextSearch;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Tests;

public sealed class DrugAutoCompleteRankerTests
{
    [Fact]
    public void FilterAndSort_PrefersExactThenPrefixThenContains()
    {
        var catalog = new[]
        {
            new TestDrug("other", "其他药品"),
            new TestDrug("amxl", "奥美拉唑肠溶胶囊"),
            new TestDrug("amxl-lite", "奥美拉唑"),
        };

        var ranked = DrugAutoCompleteRanker.FilterAndSort(
            catalog,
            "奥美拉唑",
            static item => item.Raw,
            static item => item.Display);

        Assert.Equal("amxl-lite", ranked[0].Raw);
        Assert.Equal("amxl", ranked[1].Raw);
    }

    [Fact]
    public void ScorePinyin_PrefersExactThenPrefixThenContinuousThenSubsequence()
    {
        const string drug = "盐酸溴己新注射液";
        var initials = PinyinInitialMatcher.GetInitials(drug);

        Assert.Equal(490, PinyinInitialMatcher.ScorePinyin(initials, drug));
        Assert.Equal(480, PinyinInitialMatcher.ScorePinyin(initials[..3], drug));
        Assert.Equal(470, PinyinInitialMatcher.ScorePinyin("xjx", drug));
        Assert.Equal(460, PinyinInitialMatcher.ScorePinyin("ysjx", drug));
    }

    [Fact]
    public void FilterAndSort_ReturnsFullCatalogWhenSearchEmpty()
    {
        var catalog = new[]
        {
            new TestDrug("a", "A"),
            new TestDrug("b", "B"),
        };

        var ranked = DrugAutoCompleteRanker.FilterAndSort(
            catalog,
            null,
            static item => item.Raw,
            static item => item.Display);

        Assert.Equal(2, ranked.Count);
        Assert.Equal("a", ranked[0].Raw);
        Assert.Equal("b", ranked[1].Raw);
    }

    [Fact]
    public void FilterAndSort_am_prefers_aomeilazuo_over_diaosiming()
    {
        var catalog = new[]
        {
            new TestDrug("地奥司明片", "地奥司明片"),
            new TestDrug("奥美拉唑胶囊", "奥美拉唑胶囊"),
            new TestDrug("奥美拉唑肠溶胶囊", "奥美拉唑肠溶胶囊"),
        };

        var ranked = DrugAutoCompleteRanker.FilterAndSort(
            catalog,
            "am",
            static item => item.Raw,
            static item => item.Display);

        Assert.True(ranked.Count >= 2);
        Assert.Contains("奥美", ranked[0].Raw, StringComparison.Ordinal);

        var diaoScore = PinyinInitialMatcher.Score("am", "地奥司明片");
        var aoScore = PinyinInitialMatcher.Score("am", "奥美拉唑胶囊");
        Assert.True(aoScore > diaoScore, $"奥美拉唑({aoScore}) should beat 地奥司明({diaoScore})");
    }

    [Fact]
    public void RefreshVisibleOptions_DoesNotMutateCollectionForSelectedCandidateText()
    {
        var selected = new OptionItem("奥美拉唑胶囊", "奥美拉唑胶囊");
        var visible = new System.Collections.ObjectModel.ObservableCollection<OptionItem>
        {
            selected,
            new("奥美拉唑肠溶胶囊", "奥美拉唑肠溶胶囊"),
        };
        var catalog = new[]
        {
            visible[1],
            selected,
        };
        var changes = 0;
        visible.CollectionChanged += (_, _) => changes++;

        AutoCompleteHelper.RefreshVisibleOptions(visible, catalog, selected.Display);

        Assert.Equal(0, changes);
        Assert.Same(selected, visible[0]);
    }

    private sealed record TestDrug(string Raw, string Display);
}
