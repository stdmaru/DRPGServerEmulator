using System.Buffers;
using DRPGServer.Network.Protocol;

namespace DRPGServer.Tests;

public class ProtocolTests
{
    [Fact]
    public void Crc16_matches_captured_packets()
    {
        // S2C 0xC4 (vazio) e 0x6B capturados do servidor oficial
        Assert.Equal(0, Crc16.Compute([]));
        Assert.Equal(59512, Crc16.Compute(Convert.FromHexString("A045D8398B599F439F78C0FC0C27A7551000000000000000")));
    }

    [Fact]
    public void Writer_builds_header_with_full_size()
    {
        var w = new PacketWriter();
        w.WriteU32(0xDEADBEEF);
        w.WriteZeros(70_000 / 10);
        var frame = w.ToFrame(0x152);
        var header = PacketHeader.Read(frame);
        Assert.Equal(0x00CC, frame[0] | frame[1] << 8);
        Assert.Equal(0x152, header.Opcode);
        Assert.Equal((uint)frame.Length, header.Size);   // u32, sem o corte em ushort do protótipo
        Assert.Equal(0u, header.Sequence);               // o oficial manda sempre 0
        Assert.Equal(Crc16.Compute(frame.AsSpan(14)), header.Crc);
    }

    [Fact]
    public void Writer_rejects_packets_over_client_limit()
    {
        var w = new PacketWriter();
        w.WriteZeros(PacketHeader.MaxPacketSize);
        Assert.Throws<InvalidOperationException>(() => w.ToFrame(1));
    }

    [Fact]
    public void Fixed_strings_truncate_and_stop_at_nul()
    {
        var w = new PacketWriter();
        w.WriteFixedString("abcdefghij", 5);
        var payload = w.Payload.ToArray();
        Assert.Equal("abcd\0"u8.ToArray(), payload);

        var r = new PacketReader(new byte[] { (byte)'h', (byte)'i', 0, (byte)'x', (byte)'y' });
        Assert.Equal("hi", r.ReadFixedString(5));
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void Reader_throws_on_short_payload()
    {
        var r = new PacketReader(new byte[3]);
        Assert.Throws<PacketFormatException>(() => r.ReadU32());
    }

    [Fact]
    public void Framer_handles_split_and_merged_frames()
    {
        var a = Frame(0x20, 6);
        var b = Frame(0xC4, 14);
        var all = a.Concat(b).ToArray();

        // dividido em três pedaços
        var seq = Segments(all[..5], all[5..20], all[20..]);
        Assert.Equal(FrameStatus.Complete, PacketFramer.TryRead(ref seq, out var f1, out _));
        Assert.Equal(FrameStatus.Complete, PacketFramer.TryRead(ref seq, out var f2, out _));
        Assert.Equal(FrameStatus.Incomplete, PacketFramer.TryRead(ref seq, out _, out _));
        Assert.Equal(0x20, f1!.Opcode);
        Assert.Equal(6, f1.Payload.Length);
        Assert.Equal(0xC4, f2!.Opcode);

        var partial = new ReadOnlySequence<byte>(a[..^1]);
        Assert.Equal(FrameStatus.Incomplete, PacketFramer.TryRead(ref partial, out _, out _));
    }

    [Theory]
    [InlineData(0x00CD, 20)]     // assinatura errada
    [InlineData(0x00CC, 13)]     // menor que o cabeçalho
    [InlineData(0x00CC, 0x3081)] // maior que o limite do cliente
    public void Framer_rejects_corrupt_stream(int signature, int size)
    {
        var bytes = new byte[14];
        bytes[0] = (byte)signature; bytes[1] = (byte)(signature >> 8);
        BitConverter.TryWriteBytes(bytes.AsSpan(4), (uint)size);
        var seq = new ReadOnlySequence<byte>(bytes);
        Assert.Equal(FrameStatus.Invalid, PacketFramer.TryRead(ref seq, out _, out var error));
        Assert.NotNull(error);
    }

    private static byte[] Frame(ushort opcode, int payload)
    {
        var w = new PacketWriter();
        w.WriteZeros(payload);
        return w.ToFrame(opcode);
    }

    private static ReadOnlySequence<byte> Segments(params byte[][] parts)
    {
        var first = new Segment(parts[0]);
        var last = first;
        foreach (var part in parts[1..]) last = last.Append(part);
        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(byte[] data) => Memory = data;

        public Segment Append(byte[] data)
        {
            var next = new Segment(data) { RunningIndex = RunningIndex + Memory.Length };
            Next = next;
            return next;
        }
    }
}
