using System.Buffers.Binary;
using System.Text;

namespace DRPG.Shared.Formats.Sprite;

/// <summary>One range of frames of an animation: the frames <c>Start</c> .. <c>Start + Count − 1</c> of the sprite.</summary>
public readonly record struct AniRange(int Start, int End, int Count);

/// <summary>The anchor of one frame (✅ the tamer sprites are drawn with it: <c>x − AnchorX, y − AnchorY</c>).</summary>
public readonly record struct AniFrame(int Unknown, int AnchorX, int AnchorY);

/// <summary>
/// A <c>Sprite\*.ani</c> file (✅ v322 <c>0x0086e8c0</c>, docs/cliente/formato-ani.md): which frames of the matching
/// <c>.spr</c> make up each of the 20 animations in each of the 8 directions. Everything is read with bounds checks:
/// a bad file is an <see cref="AniFormatException"/>, never an out-of-range read.
/// </summary>
public sealed class AniFile
{
    public const string Signature = "DCANIMATION0.5";
    public const int AnimationCount = 20;
    public const int DirectionCount = 8;

    private const int NameOffset = 0x0e, NameBytes = 24, FrameCountOffset = 0x26, FramesOffset = 0x2a;
    private const int FrameBytes = 12, RangeBytes = 6;

    private readonly AniRange[] _ranges;

    private AniFile(string spriteName, AniFrame[] frames, AniRange[] ranges)
    {
        SpriteName = spriteName;
        Frames = frames;
        _ranges = ranges;
    }

    /// <summary>Name of the sprite the animations index, without the folder or the <c>.spr</c> (❓ read from the header).</summary>
    public string SpriteName { get; }

    public IReadOnlyList<AniFrame> Frames { get; }

    /// <summary>
    /// The range of an animation in a direction. ✅ <c>0x00873110</c> / <c>0x008730e0</c> (start, count); animation 15 is
    /// treated as 5 (❓ <c>0xf</c> in the original). Numbers outside 0..19 and 0..7 give an empty range.
    /// </summary>
    public AniRange Range(int animation, int direction)
    {
        if (animation == 0xf)
            animation = 5;
        if (animation is < 0 or >= AnimationCount || direction is < 0 or >= DirectionCount)
            return default;
        return _ranges[animation * DirectionCount + direction];
    }

    public static AniFile Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < FramesOffset)
            throw new AniFormatException("ani: file too short for the header");
        if (!data[..Signature.Length].SequenceEqual(Encoding.ASCII.GetBytes(Signature)))
            throw new AniFormatException("ani: wrong signature");

        var nameBytes = data.Slice(NameOffset, NameBytes);
        int end = nameBytes.IndexOf((byte)0);
        var name = Encoding.ASCII.GetString(end < 0 ? nameBytes : nameBytes[..end]);
        if (name.Length == 0)
            throw new AniFormatException("ani: the sprite name is empty");
        if (name.AsSpan().IndexOfAny('\\', '/', ':') >= 0 || name.Contains(".."))
            throw new AniFormatException("ani: the sprite name has a path");
        if (name.EndsWith(".spr", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];

        uint declared = BinaryPrimitives.ReadUInt32LittleEndian(data[FrameCountOffset..]);
        long needed = FramesOffset + (long)declared * FrameBytes + AnimationCount * DirectionCount * RangeBytes;
        if (needed > data.Length)
            throw new AniFormatException($"ani: {declared} frames need {needed} bytes, the file has {data.Length}");

        int count = (int)declared;
        var frames = new AniFrame[count];
        for (int i = 0; i < count; i++)
        {
            var frame = data[(FramesOffset + i * FrameBytes)..];
            frames[i] = new AniFrame(
                BinaryPrimitives.ReadInt32LittleEndian(frame),
                BinaryPrimitives.ReadInt32LittleEndian(frame[4..]),
                BinaryPrimitives.ReadInt32LittleEndian(frame[8..]));
        }

        var table = data[(FramesOffset + count * FrameBytes)..];
        var ranges = new AniRange[AnimationCount * DirectionCount];
        for (int i = 0; i < ranges.Length; i++)
        {
            var entry = table[(i * RangeBytes)..];
            ranges[i] = new AniRange(
                BinaryPrimitives.ReadUInt16LittleEndian(entry),
                BinaryPrimitives.ReadUInt16LittleEndian(entry[2..]),
                BinaryPrimitives.ReadUInt16LittleEndian(entry[4..]));
        }
        return new AniFile(name, frames, ranges);
    }
}

public sealed class AniFormatException(string message) : Exception(message);
