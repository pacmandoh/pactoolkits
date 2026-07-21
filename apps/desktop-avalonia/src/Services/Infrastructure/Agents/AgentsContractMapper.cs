using System;
using System.Collections.Generic;
using PacToolkits.Agents.Contracts.Models;
using AppDtos = PacToolkits.Application.DTOs;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Agents;

internal static class AgentsContractMapper
{
    public static AppDtos.AgentsConfigDto ToApplication(AgentsOptions source)
        => new()
        {
            ExecutablePath = source.ExecutablePath,
            ProcessName = source.ProcessName,
            Injector = ToApplication(source.Injector),
        };

    public static AppDtos.InjectorOptionsDto ToApplication(InjectorOptions source)
        => new()
        {
            Enabled = source.Enabled,
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

    public static AgentsOptions ToContract(AppDtos.AgentsConfigDto source)
        => new()
        {
            ExecutablePath = source.ExecutablePath,
            ProcessName = source.ProcessName,
            Injector = ToContract(source.Injector),
        };

    public static InjectorOptions ToContract(AppDtos.InjectorOptionsDto source)
        => new()
        {
            Enabled = source.Enabled,
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
}
