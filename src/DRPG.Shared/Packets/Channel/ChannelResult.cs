using DRPG.Shared.Protocol;

namespace DRPG.Shared.Packets.Channel;

/// <summary>Error codes of the channel replies (<c>i32 result, i32 code</c> at the start of each one).</summary>
public static class ChannelError
{
    public const int None = 0;
    public const int InvalidCredentials = 101;
    public const int NameTaken = 102;
}

/// <summary>
/// ✅ Start of every channel reply: <c>i32 result</c> (2 success, 3 error) and <c>i32 code</c> (0 on success).
/// </summary>
public static class ChannelResult
{
    private const int Success = 2;
    private const int Failure = 3;

    public static void Write(PacketWriter w, int errorCode)
    {
        w.WriteI32(errorCode == ChannelError.None ? Success : Failure);
        w.WriteI32(errorCode);
    }

    /// <summary>Reads the code; a result that does not agree with it (2 with an error, 3 without) is malformed.</summary>
    public static int Read(PacketReader r)
    {
        var result = r.ReadI32();
        var errorCode = r.ReadI32();
        var expected = errorCode == ChannelError.None ? Success : Failure;
        if (result != expected)
            throw new PacketFormatException($"channel result {result} with code {errorCode}");
        return errorCode;
    }
}
