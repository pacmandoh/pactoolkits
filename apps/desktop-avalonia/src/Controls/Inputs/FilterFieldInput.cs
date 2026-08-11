using Avalonia.Controls;
using global::Avalonia.Layout;

namespace PacToolkits.Desktop.Avalonia.Controls;

public class FilterFieldInput : ContentControl
{
    static FilterFieldInput()
    {
        HorizontalAlignmentProperty.OverrideDefaultValue<FilterFieldInput>(HorizontalAlignment.Stretch);
        VerticalAlignmentProperty.OverrideDefaultValue<FilterFieldInput>(VerticalAlignment.Stretch);
    }
}
