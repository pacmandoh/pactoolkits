namespace PacToolkits.Desktop.Avalonia.Common;

public static class DrugAutoCompleteFilterPolicy
{
    public static bool HasDrugText(string? drugText)
        => !string.IsNullOrWhiteSpace((drugText ?? string.Empty).Trim());
}
