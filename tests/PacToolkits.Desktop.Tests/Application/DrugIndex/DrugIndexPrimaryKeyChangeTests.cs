using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Tests;

public sealed class DrugIndexPrimaryKeyChangeTests
{
    [Fact]
    public void ApplySaved_updates_fields_without_replacing_row_instance()
    {
        var dto = new DrugIndexDto(
            DrugId: "DrugA",
            Spec: "1g",
            Qty: 10,
            RuleKey: null,
            PreTc: "869",
            Pos: null,
            Note: "old",
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: null,
            Version: 1);

        var row = new DrugIndex.DrugRow(dto);
        var saved = dto with { Note = "弃用", PreTc = "870", Version = 2 };

        var before = row;
        Assert.True(row.ApplySaved(saved));
        Assert.Same(before, row);
    }
}
