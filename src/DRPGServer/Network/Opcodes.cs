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
    ChatNormal = 0x21,             // ✅ S2C (277 B): chat normal branco (21 B remetente + 256 B mensagem; DigimonRPG.bin @ 0x00ABE221)
    ChatNotice = 0x22,             // ✅ S2C (277 B): chat colorido de destaque (21 B remetente + 256 B mensagem; DigimonRPG.bin @ 0x00ABE247)
    ExitGame = 0x2B,
    JoinHandshakeRes = 0x33,
    BattleActionReq = 0x35,
    JoinHandshakeReq = 0x49,
    SystemMessage = 0x4F,          // ✅ S2C (257 B): aviso de sistema cinza no chat (u8 tipo + char[256]; ex.: "You gained the Title of [First Win].")
    NpcDialogChoice = 0x56,
    BattleStartReq = 0x61,
    BattleStartRes = 0x62,
    BattleCreate = 0x63,
    BattleSummon = 0x64,           // S2C ✅ digimon do time invocado na batalha (u8 2, u8 posição, bloco)
    BattleFleeDigimon = 0x65,      // S2C ✅ serial do digimon que fugiu
    BattleActionResults = 0x67,
    BattleLockActionBar = 0x68,
    BattleBegin = 0x69,
    BattleFleeTamer = 0x6A,        // S2C ✅ serial do tamer que fugiu
    BattleActionCooldown = 0x6B,
    BattleEnd = 0x6D,
    BattleRewardBitExp = 0x6E,
    BattleLevelUp = 0x70,
    DigimonAddAbilityPoint = 0x71,
    DigimonSkillUpgrade = 0x72,    // ✅ subir skill: bloco (uid, serial), u8 estágio, u8 skill (1/2)
    DigimonEvolved = 0x80,         // S2C ✅ forma nova ao subir de nível (rookie no 11, champion no 21 em batalha)
    BattleEvolve = 0x81,           // S2C ✅ evolução escolhida no menu, quando a barra enche
    DigimonFormInfo = 0x82,        // ✅ janela de evolução: u32 estágio (0 = atual), u32 0, bloco
    BattleDigimonStatus = 0x84,    // S2C ✅ bloco de status do digimon no fim da batalha
    ItemTeleport = 0x85,           // ✅ C2S 138 B → S2C 124 B: teleporte no mesmo mapa via item 900/907 (Teleport / Teleport X)
    DepositoryAction = 0x89,       // ✅ Digimon Depository: abrir (1), depositar (2), retirar (3), trocar (4) (736 B)
    DepositoryList = 0x8A,         // S2C ✅ Digimon Depository: slots disponíveis + 10 × 80 B (808 B)
    Unknown73 = 0x73,
    Unknown74 = 0x74,
    Unknown7E = 0x7E,
    Unknown7F = 0x7F,
    CardMove = 0x90,               // ✅ mover/equipar/desequipar/coletar card (76 B)
    ItemDropGet = 0x91,            // ✅ mover item: do chão para o inventário ou dentro do inventário
    DepositoryExpand = 0x94,       // ✅ Digimon Depository: expansão de slot por item 901/929 (S2C 1 B)
    DigimonTitleList = 0x97,       // ✅ C2S 1622 B → S2C 1608 B: janela de títulos do digimon (até 200 títulos)
    DigimonTitleEquip = 0x99,      // ✅ C2S 26 B → S2C 12 B: equipar/desequipar título do digimon
    EvolutionList = 0x9E,          // ✅ C2S uid; S2C u8 1, u8 0, u16 × 3 formas liberadas
    BattleCardEquip = 0xA0,        // ✅ C2S (98 B): equipar card no slot 0..2 de batalha (sem resposta S2C)
    BattleCardUnequip = 0xA1,      // ✅ C2S (98 B): desequipar card do slot 0..2 de batalha (sem resposta S2C)
    TeleportCharacter = 0xB0,
    TamerExp = 0xB1,               // S2C ✅ nível e EXP do tamer no fim da batalha
    RefreshDigimonStatus = 0xB2,
    SingleDigimonInfo = 0xB4,      // C2S ✅ cliente saiu da tela de resultado (6,6 s depois do 0x6D); S2C eco
    ItemDestroy = 0xBD,            // ✅ jogar item no chão (destrói)
    KeepAlive = 0xC4,
    BattleItemReceive = 0xC8,      // S2C ✅ item na bolsa de batalha (registro de 76)
    InventoryItemReceive = 0xC9,
    CashDepositoryAuth = 0xCA,     // ✅ Cash Depository: autenticar/abrir janela 0x46 (C2S 117 B → S2C 103 B)
    CashDepositoryWithdraw = 0xDD, // ✅ Cash Depository: retirar card (1) ou item (2) (C2S 206 B → S2C 192 B)
    CashDepositoryList = 0xDE,     // ✅ Cash Depository: listar 24 cards + 24 items (C2S 4434 B → S2C 4420 B)
    ExpEvent = 0xE6,
    Rename = 0xF0,                 // ✅ renomear: u8 resultado, u8 tipo (1 tamer, 2 digimon), u32 uid, nome[20]
    SetLeader = 0xF1,              // ✅ trocar o líder: u32 0, u32 personagem, u32 digimon
    InventoryRefresh = 0xFC,
    ItemUse = 0x41,                // ✅ usar item: u32 3, registro, bloco do digimon alvo (Taming Net: selvagem → capturado)
    ItemUseFailed = 0xC3,          // S2C ✅ vazio: o item não pôde ser usado
    DigimonReceived = 0xEF,        // S2C ✅ digimon novo no time (bloco)
    BattleCapture = 0xB3,          // S2C ✅ resultado da captura em batalha (2 = falhou)
    Unknown121 = 0x121,
    InventorySort = 0x148,         // ✅ C2S botão de auto-organizar inventário (u32 0, u32 0, u16 bolsa, u16 1)
    Unknown152 = 0x152,
    CashShopOpen = 0x159,          // ✅ Cash Shop: abrir loja e consultar saldo (C2S 78 B → S2C 64 B)
    CashShopBuy = 0x160,           // ✅ Cash Shop: comprar produto (C2S 70 B → S2C 56 B)
    CashShopHistory = 0x161,       // ✅ Cash Shop: histórico de compras (C2S 1006 B → S2C 992 B)
    PartySwap = 0x173,             // ✅ botão k troca as posições k + 1 e k + 2 do time
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
}
