namespace PacToolkits.Desktop.Avalonia.Common;

public static class DrugLabel
{
    public static string Format(string drugId, string spec)
        => $"{drugId}({spec})";

    public static string WithQty(string drugId, string spec, int qty)
        => $"{Format(drugId, spec)} * {qty}";
}
