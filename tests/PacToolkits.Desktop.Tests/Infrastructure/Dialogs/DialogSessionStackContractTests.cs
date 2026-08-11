using System.Collections;
using System.Reflection;
using ShadUI;

namespace PacToolkits.Desktop.Tests;

/// <summary>
/// 防止 ShadUI 升级后 <c>DialogSessionStack</c> 反射契约静默失效
/// </summary>
public sealed class DialogSessionStackContractTests
{
    private static readonly string[] CallbackFieldNames =
    [
        "OnSuccessCallbacks",
        "OnSuccessWithContextCallbacks",
        "OnSuccessAsyncCallbacks",
        "OnSuccessWithContextAsyncCallbacks",
        "OnCancelCallbacks",
        "OnCancelAsyncCallbacks"
    ];

    [Fact]
    public void ShadUI_DialogManager_reflection_contract_is_intact()
    {
        var managerType = typeof(DialogManager);
        const BindingFlags instanceNonPublic = BindingFlags.Instance | BindingFlags.NonPublic;

        AssertField(managerType, "Dialogs", instanceNonPublic);
        AssertField(managerType, "CustomDialogs", instanceNonPublic);

        var manager = new DialogManager();
        foreach (var fieldName in CallbackFieldNames)
        {
            var field = AssertField(managerType, fieldName, instanceNonPublic);
            Assert.IsAssignableFrom<IDictionary>(field.GetValue(manager));
        }

        Assert.NotNull(managerType.GetMethod("CloseDialog", instanceNonPublic));

        var simpleDialog = managerType.Assembly.GetType("ShadUI.SimpleDialog");
        Assert.NotNull(simpleDialog);
    }

    private static FieldInfo AssertField(Type type, string name, BindingFlags flags)
    {
        var field = type.GetField(name, flags);
        Assert.NotNull(field);
        return field!;
    }
}
