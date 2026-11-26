using System.Text;
using DRPG.Shared.Protocol;
using Microsoft.Extensions.Logging;

namespace DRPG.Server.Network;

/// <summary>
/// Log de pacotes. Nome do opcode em Debug; hexdump em Trace.
/// Ligue com "Logging:LogLevel:DRPG.Server.Network.PacketTrace": "Trace".
/// </summary>
public sealed class PacketTrace(ILogger<PacketTrace> log)
{
    public void Incoming(Session session, IncomingFrame frame)
    {
        if (!log.IsEnabled(LogLevel.Debug)) return;
        log.LogDebug("<- {Session} {Opcode} {Size}B", session, OpcodeNames.Of(session.Role, frame.Opcode), frame.Header.Size);
        if (log.IsEnabled(LogLevel.Trace))
            log.LogTrace("{Dump}", HexDump(frame.Payload));
    }

    public void Outgoing(Session session, ushort opcode, byte[] frame)
    {
        if (!log.IsEnabled(LogLevel.Debug)) return;
        log.LogDebug("-> {Session} {Opcode} {Size}B", session, OpcodeNames.Of(session.Role, opcode), frame.Length);
        if (log.IsEnabled(LogLevel.Trace))
            log.LogTrace("{Dump}", HexDump(frame.AsSpan(PacketHeader.Length)));
    }

    public static string HexDump(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < data.Length; i += 16)
        {
            var line = data.Slice(i, Math.Min(16, data.Length - i));
            sb.Append($"{i:x4}  ");
            for (int j = 0; j < 16; j++)
                sb.Append(j < line.Length ? $"{line[j]:x2} " : "   ");
            sb.Append(' ');
            foreach (byte b in line)
                sb.Append(b is >= 32 and < 127 ? (char)b : '.');
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
