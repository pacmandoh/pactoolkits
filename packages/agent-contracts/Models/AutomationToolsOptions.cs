namespace PacToolkits.Agent.Contracts.Models;

public sealed class AutomationToolsOptions
{
    public AhkToolOptions Ahk { get; set; } = new();
    public AgentToolOptions Agent { get; set; } = new();
}

public sealed class AhkToolOptions
{
    public string ExecutablePath { get; set; } = @".\Tools\pacinjector.exe";
    public string ProcessName { get; set; } = string.Empty;
}

public sealed class AgentToolOptions
{
    public string PgDriver { get; set; } = "PostgreSQL Unicode(x64)";
    public string PgSsl { get; set; } = "disable";
    public string OptWindowClass { get; set; } = "TFrm_mzcffy";
    public string IptWindowClass { get; set; } = "Tfrm_wzzsm";
    public Dictionary<string, int> AppWin { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["互慧软件.exe"] = 1,
        ["ProjectMain.exe"] = 1,
    };
    public int ConfirmTimeoutMs { get; set; } = 2500;
    public List<string> ColSpecs { get; set; } =
    [
        "?追溯码",
        "物资名称||药品名称",
        "规格||药品规格",
        "数量",
        "?单位",
        "?拆零标签||拆零",
    ];
    public List<string> IntCols { get; set; } = ["数量"];
    public string OptParseGridClassNN { get; set; } = "TcxGridSite2";
    public string OptVerifyGridClassNN { get; set; } = "TcxGridSite2";
    public string IptParseGridClassNN { get; set; } = "TcxGridSite2";
    public string IptVerifyGridClassNN { get; set; } = "TcxGridSite1";
    public string OptInputClassNN { get; set; } = "TMemo2";
    public string IptInputClassNN { get; set; } = "TEdit1";
    public bool WarehouseEnabled { get; set; }
    public List<string> WarehouseAnchorTexts { get; set; } = ["患者姓名", "应扫次数"];
    public string CodePickPolicy { get; set; } = "MAX_LEVEL";
    public string WarehouseTaskIdentifier { get; set; } = "单据号||当前编号";
}
