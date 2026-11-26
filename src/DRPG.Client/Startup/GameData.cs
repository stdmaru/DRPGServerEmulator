using DRPG.Shared.Formats.Pack;

namespace DRPG.Client.Startup;

/// <summary>
/// What the loading screen loads (✅ v322 <c>0x00ad3d30</c>: tables and resources): the sprite pack, the table pack and
/// the language files of <c>Language\</c> as they are on disk. Read-only: nothing here is ever written back.
/// </summary>
public sealed record GameData(PackArchive Resource, PackArchive Table, IReadOnlyDictionary<string, byte[]> Language);
