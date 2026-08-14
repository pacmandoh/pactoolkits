namespace PacToolkits.Api.Hosting;

/// <summary>
/// HTTP 协议版本（SemVer）
///
/// 由清单 components.api.contractVersion 生成
/// 客户端以该常量做协议兼容判断；PacAPI 程序版本与 SchemaBounds 不参与
/// </summary>
public static class ApiContract
{
    public const string Version = "1.3.0";
}
