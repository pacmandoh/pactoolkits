using System.Reflection;
using PacToolkits.Desktop.Avalonia.Behaviors;

namespace PacToolkits.Desktop.UiTests;

/// <summary>焦点模型公开面：FocusClear / TabScope 附着属性契约</summary>
public sealed class FocusModelContractTests
{
    [Fact]
    public void FocusClear_exposes_enable_and_suppress_grid_clear()
    {
        Assert.NotNull(FocusClear.EnableProperty);
        Assert.NotNull(FocusClear.SuppressGridClearProperty);

        var setSuppress = typeof(FocusClear).GetMethod(
            "SetSuppressGridClear",
            BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(setSuppress);
    }

    [Fact]
    public void TabScope_exposes_enable_root_and_combo_flags()
    {
        Assert.NotNull(TabScope.EnableProperty);
        Assert.NotNull(TabScope.RootNameProperty);
        Assert.NotNull(TabScope.IncludeComboBoxProperty);
    }
}
