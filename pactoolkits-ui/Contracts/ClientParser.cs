using System;

namespace pactoolkits_ui.Contracts;

public static class ClientParser
{
    public static ClientInfo Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new ClientInfo(
                Raw: raw,
                Display: "(unknown)",
                Machine: null,
                User: null,
                Ip: null,
                Os: null,
                Version: null
            );
        }

        var parts = raw.Split('|', StringSplitOptions.TrimEntries);

        string? machine = parts.Length > 0 ? parts[0] : null;
        string? user = parts.Length > 1 ? parts[1] : null;

        string? ip = null;
        string? os = null;
        string? ver = null;

        for (var i = 2; i < parts.Length; i++)
        {
            var p = parts[i];
            var eq = p.IndexOf('=');
            if (eq <= 0) continue;

            var key = p[..eq].ToLowerInvariant();
            var val = p[(eq + 1)..];

            switch (key)
            {
                case "ip": ip = val; break;
                case "os": os = val; break;
                case "ver": ver = val; break;
            }
        }

        var display = machine is not null && user is not null
            ? $"{machine} | {user}"
            : raw;

        return new ClientInfo(
            Raw: raw,
            Display: display,
            Machine: machine,
            User: user,
            Ip: ip,
            Os: os,
            Version: ver
        );
    }
}
