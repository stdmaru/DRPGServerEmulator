using DRPGServer.Game;
using DRPGServer.Game.Entities;
using DRPGServer.Network.Protocol;

namespace DRPGServer.Network.Packets.Map;

/// <summary>0x02: dados completos do personagem e do digimon líder. Obrigatório para carregar o mapa.</summary>
public sealed class CharacterDataPacket(Character character) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.CharacterData;

    protected override void Write(PacketWriter w)
    {
        var c = character;
        w.WriteU32(c.Uid);
        c.Serial.Write(w);
        w.WriteFixedString(c.Name, 21);
        w.WriteU8(c.TamerId);
        w.WriteU16(1);                          // ?
        w.WriteU16(c.Level);
        w.WriteZeros(2);
        w.WriteI32(0);                          // fama
        w.WriteI32(0);                          // EXP do tamer ❓ não persistido
        w.WriteI32(10);                         // EXP para o próximo nível do tamer ❓
        w.WriteZeros(4);
        w.WriteF64(c.Bits);
        w.WriteI32(c.TotalWins);
        w.WriteI32(c.TotalBattles);
        w.WriteI16(0);                          // vitórias ranqueadas
        w.WriteI16(0);                          // batalhas ranqueadas
        w.WriteI32(1800);                       // pontos ranqueados
        w.WriteU8((byte)Rank.ChromeDigizoid);
        w.WriteZeros(1151);

        // 1240: 5 blocos de status (✅ v322: o fim do pacote andou 5 × 8 bytes). Só o líder por ora.
        // O líder usa o mesmo começo do bloco de status, com 64..191 próprio.
        int block = w.Position;
        var d = c.Leader;
        DigimonStatusBlock.WriteHead(w, d);
        w.WriteZeros(52);                       // 64
        w.WriteI16(0);                          // 116 bônus de ATK (taxa)
        w.WriteI16(0);                          // 118 bônus de DEF (taxa)
        w.WriteI16(0);                          // 120 bônus de BR (taxa)
        w.WriteZeros(38);                       // 122
        w.WriteU8(1);                           // 160 tipo de evolução (in-training, rookie...)
        w.WriteU8(0);                           // 161 ?
        w.WriteSparse(30, (0, [0x01]));         // 162
        DigimonStatusBlock.WriteCombat(w, d);
        DigimonStatusBlock.WriteProgress(w, d);
        DigimonStatusBlock.WriteSkills(w, d);
        w.WriteI32(0);                          // 328 bônus de STR
        w.WriteI32(0);                          // bônus de AGI
        w.WriteI32(0);                          // bônus de CON
        w.WriteI32(0);                          // bônus de INT
        w.WriteZeros(DigimonStatusBlock.Length - (w.Position - block));
        for (int i = 1; i < 5; i++) DigimonStatusBlock.WriteEmpty(w);

        w.WriteI16(c.X);                        // 4720 na v322 (4680 na v242)
        w.WriteI16(c.Y);
        // ❓ capturado: 0x1852..0x1856 e 0x2052..0x205A parecem ponteiros/handles do servidor oficial
        w.WriteSparse(2876,
            (1852, [0xDC, 0xF3, 0x51, 0x68, 0x01]),
            (2052, [0xFA, 0x11, 0x00, 0x00, 0x50, 0xB8, 0x52, 0x68]),
            (2792, [0x0A]));
    }
}

/// <summary>0x10: resposta ao 0x04 (o cliente pede ao conectar no global).</summary>
public sealed class RefreshCharacterInfoPacket(Character character) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.RefreshCharacterInfo;

    protected override void Write(PacketWriter w)
    {
        character.Serial.Write(w);
        w.WriteFixedString(character.Name, 21);
        w.WriteU8(character.TamerId);
        w.WriteU16(1);                          // ?
        w.WriteU16(1);                          // ?
        w.WriteU16(1);                          // ?
        w.WriteI16(character.X);
        w.WriteI16(character.Y);
        w.WriteI16(0x3F);                       // ?
        w.WriteI16(0x58);                       // ?
        w.WriteI32(0);
        character.Leader.Serial.Write(w);
        w.WriteU16(character.Leader.SpeciesId);
        w.WriteFixedString(character.Leader.Name, 21);
        w.WriteU8(1);
        w.WriteI32(1);
        w.WriteZeros(104);                      // ✅ v322: 204 bytes (era 200). ❓ onde entram os 4 bytes novos
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

/// <summary>0x21: mensagem de chat normal.</summary>
public sealed class ChatNormalPacket(string nickname, string message) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.ChatNormal;

    protected override void Write(PacketWriter w)
    {
        w.WriteFixedString(nickname, 21);
        w.WriteFixedString(message, 44);
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

/// <summary>0x0E (mapa e global): primeiro pacote depois de conectar.</summary>
public sealed class HelloPacket(ushort opcode) : ServerPacket
{
    // ❓ chave de sessão capturada; o cliente não parece validar
    private static readonly byte[] SessionKey =
        [0x35, 0x5B, 0x83, 0x6D, 0xFD, 0x18, 0x58, 0x11, 0xE8, 0x0C, 0x96, 0x46, 0xBC, 0x7D, 0xBF, 0x98];

    public override ushort Opcode => opcode;
    protected override void Write(PacketWriter w) => w.WriteBytes(SessionKey);
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
        w.WriteU16(1);                          // ❓ tipo de evolução
        w.WriteU16(leader.Level);
        w.WriteU8(leader.Species.Classification);
        w.WriteU8(wild.State switch             // 1 no mapa, 6/8 morto, 11 em batalha
        {
            WildState.InBattle => 11,
            WildState.Dead => 6,
            _ => 1,
        });
        w.WriteU16((ushort)wild.Facing);
        w.WriteI16(wild.X);
        w.WriteI16(wild.Y);
        w.WriteI16(wild.X);                     // ❓ destino do movimento; no oficial é igual à posição
        w.WriteI16(wild.Y);
    }
}
