using System;
using System.Collections.Generic;
using PacToolkits.Agent.Contracts.Commands;
using PacToolkits.Agent.Contracts.Models;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Agent;

internal static class AutomationContractMapper
{
    public static AutomationConfigDto ToApplication(AutomationToolsOptions source)
        => new()
        {
            Ahk = ToApplication(source.Ahk),
            Agent = ToApplication(source.Agent)
        };

    public static AutomationAhkOptionsDto ToApplication(AhkToolOptions source)
        => new()
        {
            ExecutablePath = source.ExecutablePath,
            ProcessName = source.ProcessName,
        };

    public static AutomationAgentOptionsDto ToApplication(AgentToolOptions source)
        => new()
        {
            PgDriver = source.PgDriver,
            PgSsl = source.PgSsl,
            OptWindowClass = source.OptWindowClass,
            IptWindowClass = source.IptWindowClass,
            AppWin = new Dictionary<string, int>(source.AppWin, StringComparer.OrdinalIgnoreCase),
            ConfirmTimeoutMs = source.ConfirmTimeoutMs,
            ColSpecs = [.. source.ColSpecs],
            IntCols = [.. source.IntCols],
            OptParseGridClassNN = source.OptParseGridClassNN,
            OptVerifyGridClassNN = source.OptVerifyGridClassNN,
            IptParseGridClassNN = source.IptParseGridClassNN,
            IptVerifyGridClassNN = source.IptVerifyGridClassNN,
            OptInputClassNN = source.OptInputClassNN,
            IptInputClassNN = source.IptInputClassNN,
            WarehouseEnabled = source.WarehouseEnabled,
            WarehouseAnchorTexts = [.. source.WarehouseAnchorTexts],
            CodePickPolicy = source.CodePickPolicy,
            WarehouseTaskIdentifier = source.WarehouseTaskIdentifier,
        };

    public static AutomationToolsOptions ToContract(AutomationConfigDto source)
        => new()
        {
            Ahk = ToContract(source.Ahk),
            Agent = ToContract(source.Agent)
        };

    public static AhkToolOptions ToContract(AutomationAhkOptionsDto source)
        => new()
        {
            ExecutablePath = source.ExecutablePath,
            ProcessName = source.ProcessName,
        };

    public static AgentToolOptions ToContract(AutomationAgentOptionsDto source)
        => new()
        {
            PgDriver = source.PgDriver,
            PgSsl = source.PgSsl,
            OptWindowClass = source.OptWindowClass,
            IptWindowClass = source.IptWindowClass,
            AppWin = new Dictionary<string, int>(source.AppWin, StringComparer.OrdinalIgnoreCase),
            ConfirmTimeoutMs = source.ConfirmTimeoutMs,
            ColSpecs = [.. source.ColSpecs],
            IntCols = [.. source.IntCols],
            OptParseGridClassNN = source.OptParseGridClassNN,
            OptVerifyGridClassNN = source.OptVerifyGridClassNN,
            IptParseGridClassNN = source.IptParseGridClassNN,
            IptVerifyGridClassNN = source.IptVerifyGridClassNN,
            OptInputClassNN = source.OptInputClassNN,
            IptInputClassNN = source.IptInputClassNN,
            WarehouseEnabled = source.WarehouseEnabled,
            WarehouseAnchorTexts = [.. source.WarehouseAnchorTexts],
            CodePickPolicy = source.CodePickPolicy,
            WarehouseTaskIdentifier = source.WarehouseTaskIdentifier,
        };

    public static AutomationRunState ToApplication(ToolRunState source)
        => source switch
        {
            ToolRunState.Running => AutomationRunState.Running,
            ToolRunState.Stopped => AutomationRunState.Stopped,
            _ => AutomationRunState.Unknown
        };

    public static AutomationCommandResult ToApplication(ToolCommandResult source)
        => new(source.Ok, source.Message, source.SuppressToast);
}
