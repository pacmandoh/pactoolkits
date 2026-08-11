namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Security;

public static class UnlockScopes
{
    public const string SharedOps = "shared_sensitive_ops";

    public const string SharedOpsHint =
        "敏感操作提示：验证仅在本地进行，不会上传密码\n请输入数据库密码以解锁敏感操作";
}

