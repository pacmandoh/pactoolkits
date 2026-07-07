using Avalonia.Data;
using PacToolkits.Desktop.Avalonia.Behaviors;

namespace PacToolkits.Desktop.Avalonia.Common;

public static class DataGridBehaviorRules
{
    public static string? SortPath(BindingBase? binding)
    {
        if (binding is null)
        {
            return null;
        }

        return binding switch
        {
            Binding reflection => reflection.Path,
            _ => binding.GetType().GetProperty("Path")?.GetValue(binding)?.ToString()
        };
    }

    public static int SelectionInsertIndex(bool indexColumnFirst)
        => indexColumnFirst ? 1 : 0;

    public static DataGridIndexHeaderFace IndexHeaderFace(bool filterActive, bool hasActiveSort)
    {
        if (filterActive)
        {
            return DataGridIndexHeaderFace.ClearFilter;
        }

        if (hasActiveSort)
        {
            return DataGridIndexHeaderFace.ClearSort;
        }

        return DataGridIndexHeaderFace.Default;
    }

    public static bool? SelectAllTriState(int selectedCount, int totalCount)
    {
        if (totalCount == 0)
        {
            return false;
        }

        if (selectedCount == 0)
        {
            return false;
        }

        if (selectedCount == totalCount)
        {
            return true;
        }

        return null;
    }
}
