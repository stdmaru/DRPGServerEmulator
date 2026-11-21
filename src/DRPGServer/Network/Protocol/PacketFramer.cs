using System.Buffers;

namespace DRPGServer.Network.Protocol;

public enum FrameStatus { Incomplete, Complete, Invalid }

/// <summary>Um pacote recebido: cabeçalho e cópia do payload.</summary>
public sealed record IncomingFrame(PacketHeader Header, byte[] Payload)
{
    public ushort Opcode => Header.Opcode;
}

/// <summary>
/// Corta o fluxo TCP em pacotes. Assinatura ou tamanho inválido = conexão corrompida:
/// o chamador fecha a sessão (o cliente oficial nunca dessincroniza, ver cabecalho.md).
/// </summary>
public static class PacketFramer
{
    public static FrameStatus TryRead(ref ReadOnlySequence<byte> buffer, out IncomingFrame? frame, out string? error)
    {
        frame = null;
        error = null;
        if (buffer.Length < PacketHeader.Length)
            return FrameStatus.Incomplete;

        Span<byte> head = stackalloc byte[PacketHeader.Length];
        buffer.Slice(0, PacketHeader.Length).CopyTo(head);

        ushort signature = (ushort)(head[0] | head[1] << 8);
        if (signature != PacketHeader.Signature)
        {
            error = $"assinatura inválida 0x{signature:X4}";
            return FrameStatus.Invalid;
        }

        var header = PacketHeader.Read(head);
        if (header.Size < PacketHeader.Length || header.Size > PacketHeader.MaxPacketSize)
        {
            error = $"tamanho inválido {header.Size} (opcode 0x{header.Opcode:X})";
            return FrameStatus.Invalid;
        }

        if (buffer.Length < header.Size)
            return FrameStatus.Incomplete;

        var payload = buffer.Slice(PacketHeader.Length, header.PayloadLength).ToArray();
        buffer = buffer.Slice(header.Size);
        frame = new IncomingFrame(header, payload);
        return FrameStatus.Complete;
    }
}
