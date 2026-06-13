namespace PacToolkits.Agent.Contracts.Models;

/// <summary>
/// Strongly typed view of AutomationTools.Agent UI automation selectors.
/// </summary>
public sealed class AgentUiTargets
{
    public string OptWindowClass { get; set; } = "TFrm_mzcffy";

    public string IptWindowClass { get; set; } = "Tfrm_wzzsm";

    public ClassNnTarget OptParseGrid { get; set; } = new("TcxGridSite2");

    public ClassNnTarget OptVerifyGrid { get; set; } = new("TcxGridSite2");

    public ClassNnTarget IptParseGrid { get; set; } = new("TcxGridSite2");

    public ClassNnTarget IptVerifyGrid { get; set; } = new("TcxGridSite1");

    public ClassNnTarget OptInput { get; set; } = new("TMemo2");

    public ClassNnTarget IptInput { get; set; } = new("TEdit1");
}
