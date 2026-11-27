using DRPG.Server.Game;
using DRPG.Server.Game.Entities;
using DRPG.Server.Game.Stats;
using DRPG.Shared.Packets.Map;
using DRPG.Shared.Protocol;

namespace DRPG.Server.Network.Packets.Map;

/// <summary>
/// 0x02: dados completos do personagem e do digimon líder. Obrigatório para carregar o mapa. O pacote é o
/// <see cref="CharacterDataPacket"/> do Shared; aqui só se monta a partir do <see cref="Character"/>.
/// </summary>
public static class CharacterData
{
    public static CharacterDataPacket Of(Character c, long serverUnixTime)
    {
        // 100..747: ✅ 6 equipamentos primários (Crests 4..6 + Digimentals 7..9; conn=238/428)
        var primary = EquipmentSlots.PrimarySlots.Select(place => ItemRecord.Of(c.Inventory.EquippedAt(place))).ToArray();
        // 784..1239: ✅ 6 Card Slots equipados (conn=428/437/442/446)
        var cards = new CardData[Inventory.CardSlotCount];
        for (int slot = 0; slot < cards.Length; slot++)
            cards[slot] = CardRecord.Of(c.Inventory.CardSlots[slot]);
        // 1240: ✅ 5 blocos de status, um por posição do time (líder na 0; pode haver posição vazia, sessão 56).
        // O bloco do time manda sempre 1 em 162 (máscara de elemento).
        var party = c.Party.Select(d => d is null ? DigimonStatus.Empty : DigimonStatusBlock.Of(d, elementMask: 1)).ToArray();
        // 4724..7599: ✅ 23 slots de equipamento do Tamer (DigimonRPG.bin @ 0x00B5BF21..0x00B5C150)
        var tail = EquipmentSlots.TailSlots.Select(slot => ItemRecord.Of(c.Inventory.EquippedAt(slot.Place))).ToArray();

        return new CharacterDataPacket(
            c.Uid, c.Serial, c.Name, c.TamerId,
            Unknown42: 1,                                       // ?
            c.Level,
            Fame: 0,
            Exp: 0,                                             // EXP do tamer ❓ não persistido
            ExpToNextLevel: (int)ExpFormulas.TamerExpToNext(c.Level), // ✅ 5·(L + 1)
            c.Wallet.Bits, c.TotalWins, c.TotalBattles,
            RankedWins: 0, RankedBattles: 0,
            RankedPoints: 1800,                                 // 84 (0x54)
            Rank: (ushort)Rank.ChromeDigizoid,                  // 88 (0x58) ✅ WORD [player+0x12AC]
            c.Wallet.LadderPoints,                              // 92 (0x5C) ✅ DWORD [player+0x12B0] (DigimonRPG.bin @ 0x00B5C119)
            primary, cards, party, c.X, c.Y,                    // X/Y: 4720 na v322 (4680 na v242)
            tail,
            // 0x19A8 / 0x19AA / 0x19B0 (✅ DigimonRPG.bin @ 0x00B5C028 / 0x00B5C044 / 0x00ABE028 e captures.db conn=238/428)
            c.Wallet.Medals, c.Wallet.Coins, (uint)serverUnixTime);
    }
}

/// <summary>
/// 0x10: resumo do tamer e do líder; resposta ao 0x04 e o que o oficial manda sobre outros jogadores.
/// ✅ v322: 204 bytes (era 200). Offsets 0..100 iguais à v242 (1919 amostras v322, 49 v242).
/// ❓ Os 4 bytes novos caem depois de 100: a v322 traz um par (5, ~5000) em 128 que não aparece na v242.
/// Layout em docs/protocolo/structs/tamer-resumo.md.
/// </summary>
public sealed class RefreshCharacterInfoPacket(Character character, TamerState state = TamerState.Idle) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.RefreshCharacterInfo;

    protected override void Write(PacketWriter w)
    {
        var c = character;
        c.Serial.Write(w);                      // 0
        w.WriteFixedString(c.Name, 21);         // 16
        w.WriteU8(c.TamerId);                   // 37
        w.WriteU16(1);                          // 38 ? (1, 2, 5, 6)
        w.WriteU16(c.Level);                    // 40 nível do tamer ✅
        w.WriteU8((byte)state);                 // 42 ✅ estado: 1 parado, 5 andando, 11 em batalha (como o 0x11)
        w.WriteU8(0);                           // 43 ✅ direção (0..7, como a dos selvagens)
        w.WriteI16(c.X);                        // 44 posição
        w.WriteI16(c.Y);
        w.WriteI16(c.X);                        // 48 destino (igual à posição quando parado) ✅
        w.WriteI16(c.Y);
        w.WriteI32(0);                          // 52 sempre 0
        c.Leader.Serial.Write(w);               // 56
        w.WriteU16(c.Leader.SpeciesId);         // 72
        w.WriteFixedString(c.Leader.Name, 21);  // 74
        w.WriteU8(c.Leader.Species.Stage);      // 95 estágio do líder ✅ (1 in-training, 2 rookie, 3 champion, 5 mega)
        w.WriteI32(c.Leader.Level);             // 96 nível do líder ✅
        w.WriteU32(c.Leader.TitleId);           // 100 ✅ título equipado do líder (conn=36/76/141)
        w.WriteZeros(100);                      // até 204
    }
}

/// <summary>0xB0: muda o personagem de posição ou de mapa.</summary>
public sealed class TeleportCharacterPacket(byte mapId, short x, short y) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.TeleportCharacter;

    protected override void Write(PacketWriter w)
    {
        w.WriteI32(mapId);
        w.WriteI16(x);
        w.WriteI16(y);
    }
}

/// <summary>
/// 0x21 (277 bytes): ✅ mensagem de chat normal (21 B remetente + 256 B mensagem; DigimonRPG.bin @ 0x00ABE221 → 0x00AEDFF0).
/// Se o remetente não existe no mapa, vai direto para a janela de chat em branco sem balão de fala.
/// </summary>
public sealed class ChatNormalPacket(string nickname, string message) : ServerPacket
{
    public const int NicknameLength = 21;
    public const int MessageLength = 256;

    public override ushort Opcode => (ushort)MapOpcode.ChatNormal;

    protected override void Write(PacketWriter w)
    {
        w.WriteFixedString(nickname, NicknameLength);
        w.WriteFixedString(message, MessageLength);
    }
}

/// <summary>
/// 0x22 (277 bytes): ✅ mensagem de chat colorida no Map Server (21 B remetente + 256 B mensagem; DigimonRPG.bin @ 0x00ABE247 → 0x00AF18C0).
/// Quando o remetente não existe no mapa, imprime <c>[remetente] mensagem</c> em cor de destaque sem balão de fala —
/// ideal para avisos direcionados e echo de debug de administradores.
/// </summary>
public sealed class ChatNoticePacket(string sender, string message) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.ChatNotice;

    protected override void Write(PacketWriter w)
    {
        w.WriteFixedString(sender, ChatNormalPacket.NicknameLength);
        w.WriteFixedString(message, ChatNormalPacket.MessageLength);
    }
}

/// <summary>
/// 0x4F (257 bytes): ✅ mensagem de sistema no chat (u8 tipo [1 = aviso cinza] + char[256] texto).
/// Confirmado em 18 capturas v322 (conn=92/100/137/145/205/408/480): <c>You gained the Title of [First Win].</c>.
/// </summary>
public sealed class SystemMessagePacket(string message, byte type = 1) : ServerPacket
{
    public const int MessageLength = 256;

    public override ushort Opcode => (ushort)MapOpcode.SystemMessage;

    protected override void Write(PacketWriter w)
    {
        w.WriteU8(type);
        w.WriteFixedString(message, MessageLength);
    }
}

/// <summary>0x2B: confirma a saída do jogo. Vazio.</summary>
public sealed class ExitGamePacket : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.ExitGame;
    protected override void Write(PacketWriter w) { }
}

/// <summary>0x33: resposta ao 0x49. Oito registros de 62 bytes numerados de 1 a 8 (❓ canais/slots).</summary>
public sealed class JoinHandshakePacket : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.JoinHandshakeRes;

    protected override void Write(PacketWriter w)
    {
        w.WriteZeros(8);
        for (byte i = 1; i <= 8; i++)
        {
            w.WriteU8(i);
            w.WriteZeros(61);
        }
    }
}

/// <summary>0x0E (mapa e global): primeiro pacote depois de conectar (semente de 16 bytes da cifra global).</summary>
public sealed class HelloPacket(ushort opcode, byte[]? seed = null) : ServerPacket
{
    public static ReadOnlySpan<byte> DefaultSeed =>
        [0x35, 0x5B, 0x83, 0x6D, 0xFD, 0x18, 0x58, 0x11, 0xE8, 0x0C, 0x96, 0x46, 0xBC, 0x7D, 0xBF, 0x98];

    private readonly byte[] _seed = seed ?? DefaultSeed.ToArray();

    public override ushort Opcode => opcode;
    protected override void Write(PacketWriter w) => w.WriteBytes(_seed);
}

/// <summary>0xC4: resposta vazia ao keep-alive do cliente (channel, mapa e global).</summary>
public sealed class KeepAlivePacket(ushort opcode) : ServerPacket
{
    public override ushort Opcode => opcode;
    protected override void Write(PacketWriter w) { }
}

/// <summary>0x56: eco da escolha num diálogo de NPC (sucesso).</summary>
public sealed class NpcChoicePacket(uint choiceId) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.NpcDialogChoice;
    protected override void Write(PacketWriter w) => w.WriteU32(choiceId);
}

/// <summary>0x11: estado e posição de um grupo de digimons selvagens.</summary>
public sealed class WildDigimonUpdatePacket(WildDigimon wild) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.FieldDigimonUpdate;

    protected override void Write(PacketWriter w)
    {
        var leader = wild.Leader;
        wild.Serial.Write(w);
        w.WriteFixedString(leader.Name, 21);
        w.WriteU8(0);
        w.WriteU16(leader.SpeciesId);
        w.WriteU16(leader.Species.Stage);       // ❓ estágio (era 1 fixo)
        w.WriteU16(leader.Level);
        w.WriteU8(wild.Spawn.Grade);            // ✅ grau do grupo: 2 Beginner, 3 Veteran (como byte 300 do 0x63)
        w.WriteU8(wild.StateCode);              // ✅ 1 parado, 4 andando, 11 batalha, 8 derrotado, 12 saindo
        w.WriteU16((ushort)wild.Facing);
        w.WriteI16(wild.X);
        w.WriteI16(wild.Y);
        w.WriteI16(wild.DestX);                 // ✅ destino do movimento (igual à posição quando parado)
        w.WriteI16(wild.DestY);
    }
}
