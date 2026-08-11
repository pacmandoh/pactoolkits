namespace PacToolkits.Desktop.Avalonia.Ui.Formatting;

/// <summary>药品展示标签格式化</summary>
public static class DrugLabel
{
    public static string Format(string drugId, string spec)
        => $"{drugId}({spec})";

    public static string WithQty(string drugId, string spec, int qty)
        => $"{Format(drugId, spec)} * {qty}";
}
