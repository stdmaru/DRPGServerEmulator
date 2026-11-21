namespace DRPGServer.Network;

/// <summary>Os quatro servidores do jogo (ver docs/protocolo/servidores.md).</summary>
public enum ServerRole : byte
{
    Login = 1,
    Channel = 2,
    Map = 3,
    Global = 4,
}

public enum LoginOpcode : ushort
{
    Handshake = 0x05,
}

public enum ChannelOpcode : ushort
{
    ChannelSelect = 0x03,
    Authenticate = 0x04,
    CharacterCreate = 0x10,
    CharacterDelete = 0x11,
    CharacterList = 0x12,
    UserCheck = 0x14,
    MapConnectionConfirm = 0x16,
    ClientHardwareInfo = 0x18,     // C2S: endereços MAC do cliente; sem resposta
    KeepAlive = 0xC4,
}

public enum MapOpcode : ushort
{
    JoinCharacter = 0x01,
    CharacterData = 0x02,          // S2C, obrigatório para carregar o mapa
    InventoryData = 0x03,          // S2C, obrigatório para carregar o mapa
    RefreshCharacterInfoReq = 0x04,
    Unknown05 = 0x05,
    Unknown07 = 0x07,
    Hello = 0x0E,                  // S2C ao conectar; C2S ecoa
    RefreshCharacterInfo = 0x10,   // S2C, resposta ao 0x04
    FieldDigimonUpdate = 0x11,
    ItemDrop = 0x13,
    MoveCharacter = 0x20,
    ChatNormal = 0x21,
    ExitGame = 0x2B,
    JoinHandshakeRes = 0x33,
    BattleActionReq = 0x35,
    JoinHandshakeReq = 0x49,
    NpcDialogChoice = 0x56,
    BattleStartReq = 0x61,
    BattleStartRes = 0x62,
    BattleCreate = 0x63,
    Battle65 = 0x65,               // fuga?
    BattleActionResults = 0x67,
    BattleLockActionBar = 0x68,
    BattleBegin = 0x69,
    Battle6A = 0x6A,               // fuga?
    BattleActionCooldown = 0x6B,
    BattleEnd = 0x6D,
    BattleRewardBitExp = 0x6E,
    BattleLevelUp = 0x70,
    DigimonAddAbilityPoint = 0x71,
    Unknown73 = 0x73,
    Unknown74 = 0x74,
    Unknown7E = 0x7E,
    Unknown7F = 0x7F,
    ItemDropGet = 0x91,
    TeleportCharacter = 0xB0,
    UnknownB1 = 0xB1,
    RefreshDigimonStatus = 0xB2,
    SingleDigimonInfo = 0xB4,
    KeepAlive = 0xC4,
    InventoryItemReceive = 0xC9,
    UnknownE6 = 0xE6,
    UnknownF0 = 0xF0,
    InventoryRefresh = 0xFC,
    Unknown121 = 0x121,
    Unknown152 = 0x152,
}

public enum GlobalOpcode : ushort
{
    Hello = 0x0E,                  // S2C ao conectar; C2S ecoa
    UserAuth = 0x64,               // C2S, primeiro pacote do cliente
    Unknown65 = 0x65,
    EncyclopediaData = 0x7B,
    EncyclopediaCollection = 0x7C,
    KeepAlive = 0xC4,
    ChatWhisper = 0xC8,
    ChatMegaphone = 0xCA,
    PlayerEvent = 0xCC,
    CharacterJoin = 0xDE,
    UnknownE1 = 0xE1,              // S2C grande depois do join, seguido de 0x65
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
}
