using PacToolkits.Desktop.Avalonia.Common;

namespace PacToolkits.Desktop.Tests;

public sealed class DrugLabelTests
{
    [Fact]
    public void Format_uses_parentheses_for_spec()
    {
        Assert.Equal("阿莫西林(0.25g*24粒)", DrugLabel.Format("阿莫西林", "0.25g*24粒"));
    }

    [Fact]
    public void WithQty_appends_qty_suffix()
    {
        Assert.Equal(
            "阿莫西林(0.25g*24粒) * 24",
            DrugLabel.WithQty("阿莫西林", "0.25g*24粒", 24));
    }
}
