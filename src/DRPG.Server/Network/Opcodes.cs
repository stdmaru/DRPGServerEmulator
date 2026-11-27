using DRPG.Shared.Protocol;

namespace DRPG.Server.Network;

/// <summary>Os quatro servidores do jogo (ver docs/protocolo/servidores.md).</summary>
public enum ServerRole : byte
{
    Login = 1,
    Channel = 2,
    Map = 3,
    Global = 4,
}

public enum GlobalOpcode : ushort
{
    Hello = 0x0E,                  // S2C ao conectar; C2S ecoa
    UserAuth = 0x64,               // C2S, primeiro pacote do cliente
    Unknown65 = 0x65,
    EncyclopediaData = 0x7B,
    EncyclopediaCollection = 0x7C,
    KeepAlive = 0xC4,
    ChatWhisper = 0xC8,            // ✅ S2C: whisper privado (DigimonRPG.bin @ 0x00ACECE9)
    ChatMegaphone = 0xCA,          // ✅ S2C: megafone global (DigimonRPG.bin @ 0x00ACF039)
    PlayerEvent = 0xCC,            // ✅ S2C: eventos de jogador (DigimonRPG.bin @ 0x00ACDE87)
    CharacterJoin = 0xDE,
    UnknownE1 = 0xE1,              // S2C grande depois do join, seguido de 0x65
    ChatNotice = 0xE6,             // ✅ S2C: anúncio/aviso global colorido (4 tipos: 4..7; DigimonRPG.bin @ 0x00ACF3A4)
    UnknownCB = 0xCB,              // C2S grande depois do join
}

public static class OpcodeNames
{
    public static string Of(ServerRole role, ushort opcode)
    {
        string? name = role switch
        {
            ServerRole.Login => Enum.GetName((LoginOpcode)opcode),
            ServerRole.Channel => Enum.GetName((ChannelOpcode)opcode),
            ServerRole.Map => Enum.GetName((MapOpcode)opcode),
            ServerRole.Global => Enum.GetName((GlobalOpcode)opcode),
            _ => null,
        };
        return name is null ? $"0x{opcode:X}" : $"{name}(0x{opcode:X})";
    }

    /// <summary>Opcode pelo nome do enum do servidor (ex.: <c>Map</c> + <c>ChatNormal</c>), para configuração.</summary>
    public static bool TryParse(ServerRole role, string name, out ushort opcode)
    {
        opcode = 0;
        bool ok = role switch
        {
            ServerRole.Login => TryParse<LoginOpcode>(name, out opcode),
            ServerRole.Channel => TryParse<ChannelOpcode>(name, out opcode),
            ServerRole.Map => TryParse<MapOpcode>(name, out opcode),
            ServerRole.Global => TryParse<GlobalOpcode>(name, out opcode),
            _ => false,
        };
        return ok;
    }

    private static bool TryParse<TOpcode>(string name, out ushort opcode) where TOpcode : struct, Enum
    {
        // só nomes declarados: Enum.TryParse aceitaria também "33" ou "0x21"
        if (Enum.GetNames<TOpcode>().Contains(name) && Enum.TryParse<TOpcode>(name, out var value))
        {
            opcode = Convert.ToUInt16(value);
            return true;
        }
        opcode = 0;
        return false;
    }
}
