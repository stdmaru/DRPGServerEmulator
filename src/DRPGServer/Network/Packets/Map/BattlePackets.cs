using DRPGServer.Game;
using DRPGServer.Game.Battle;
using DRPGServer.Game.Battle.Cards;
using DRPGServer.Game.Entities;
using DRPGServer.Network.Protocol;

namespace DRPGServer.Network.Packets.Map;

/// <summary>0x62: batalha criada, com o serial que o cliente confirma de volta. ✅ v322: serial + 01 02 00 00.</summary>
public sealed class StartBattlePacket(Serial battle) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.BattleStartRes;

    protected override void Write(PacketWriter w)
    {
        battle.Write(w);
        w.WriteU8(1);                           // ? sempre 1
        w.WriteU8(2);                           // ? sempre 2
        w.WriteU16(0);
    }
}

/// <summary>0x63: os dois times (B primeiro, depois A), cinco blocos de status de 696 bytes cada.</summary>
public sealed class BattleCreatePacket(Battle battle) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.BattleCreate;

    protected override void Write(PacketWriter w)
    {
        WriteTeam(w, battle.TeamB);
        WriteTeam(w, battle.TeamA);
    }

    private static void WriteTeam(PacketWriter w, IReadOnlyList<Combatant> team)
    {
        for (int i = 0; i < Battle.MaxPerSide; i++)
        {
            if (i < team.Count) DigimonStatusBlock.Write(w, team[i].Digimon);
            else DigimonStatusBlock.WriteEmpty(w);
        }
    }
}

/// <summary>0x69: o servidor aceita o "pronto" do cliente e as barras voltam a encher. Vazio.</summary>
public sealed class BattleBeginPacket : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.BattleBegin;
    protected override void Write(PacketWriter w) { }
}

/// <summary>0x6B: valor atual da barra de ação de um digimon (✅ um por combatente a cada passo).</summary>
public sealed class ActionCooldownPacket(Serial digimon, int gauge) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.BattleActionCooldown;

    protected override void Write(PacketWriter w)
    {
        digimon.Write(w);
        w.WriteI32(gauge);
        w.WriteI32(0);                          // ? sempre 0
    }
}

/// <summary>Ação do 0x35 (C2S manda 2 = escolher; o servidor responde com as outras).</summary>
public enum BattleAction : byte
{
    Selected = 1,                               // ✅ escolha marcada
    Choose = 2,                                 // ✅ C2S: clique numa ação
    Executed = 3,                               // ✅ a barra encheu e a ação vai acontecer (ícone e alvo zerados)
    Cleared = 4,                                // ✅ escolha desmarcada (segundo clique, ou o inimigo vai agir)
}

/// <summary>Ícone do 0x35: o que o jogador escolheu.</summary>
public enum BattleIcon : ushort
{
    None = 0,
    Capture = 0x01,                             // ✅ captura comum (alvo = selvagem), até 3 por batalha
    Summon = 0x02,                              // ✅ invocar um digimon do time (alvo = serial dele)
    Flee = 0x04,                                // ✅ fugir (sem alvo)
    Attack = 0x0A,                              // ✅ ataque básico
    Skill1 = 0x0B,                              // ✅ skill 1 da forma (Hop Hip, Bunbun Punch)
    Skill2 = 0x0C,                              // ✅ skill 2 (V-mon Head)
    Evolve1 = 0x14,                             // ✅ evoluir para a 1ª forma da lista do 0x9E (sem alvo)
    Evolve2 = 0x15,
    Evolve3 = 0x16,                             // ✅ 3ª forma (Tactimon, sessão 56)
}

/// <summary>0x35 (S2C, 36 bytes): u8 ação, u16 ícone, u8 0, serial de quem age, serial do alvo.</summary>
public sealed class ActionRequestPacket(BattleAction action, BattleIcon icon, Serial requester, Serial target) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.BattleActionReq;

    protected override void Write(PacketWriter w)
    {
        w.WriteU8((byte)action);
        w.WriteU16((ushort)icon);
        w.WriteU8(0);
        requester.Write(w);
        target.Write(w);
    }
}

/// <summary>
/// 0xB3 (744 bytes): resultado da captura comum (✅ DigimonRPG.bin @ 0x00AF9A30).
/// u32 resultado (1 = sucesso, 2 = falhou), serial do tamer, u32 0, bloco de 696 (na falha só o serial do alvo em +4;
/// no sucesso, bloco do digimon capturado mantendo o MESMO serial do alvo em +4 para o cliente removê-lo da batalha e
/// adicioná-lo ao time), u32 0, serial de quem tentou, u32 0.
/// </summary>
public sealed class BattleCapturePacket(bool success, Serial tamer, Combatant actor, Combatant target, Digimon? captured) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.BattleCapture;

    protected override void Write(PacketWriter w)
    {
        w.WriteU32(success ? 1u : 2u);
        tamer.Write(w);
        w.WriteU32(0);
        if (success && captured is not null) DigimonStatusBlock.Write(w, captured);
        else
        {
            w.WriteU32(0);
            target.Digimon.Serial.Write(w);
            w.WriteZeros(DigimonStatusBlock.Length - 4 - Serial.Length);
        }
        w.WriteU32(0);
        actor.Digimon.Serial.Write(w);
        w.WriteU32(0);
    }
}

/// <summary>0x68: trava as barras durante a animação. Vazio.</summary>
public sealed class LockActionBarPacket : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.BattleLockActionBar;
    protected override void Write(PacketWriter w) { }
}

/// <summary>
/// 0x67: resultado de uma ação. ✅ Time de quem age nas posições 0..4, o outro em 5..9, 136 bytes por posição;
/// depois u32 lado (1 inimigo, 2 jogador), u32 skill (0 = ataque básico), u32 0. Layout em docs/mecanicas/batalha.md.
/// </summary>
public sealed class ActionResultsPacket(
    IReadOnlyList<Combatant> actingTeam, IReadOnlyList<Combatant> otherTeam, Combatant actor,
    IReadOnlyDictionary<Combatant, bool> targetHits, byte attackType = Data.GameData.SkillInfo.TypeMelee,
    ushort skillId = 0, IReadOnlyDictionary<Combatant, ActivatedCards>? activatedCards = null)
    : ServerPacket
{
    public const int EntryLength = 136;

    public override ushort Opcode => (ushort)MapOpcode.BattleActionResults;

    protected override void Write(PacketWriter w)
    {
        WriteTeam(w, actingTeam);
        WriteTeam(w, otherTeam);
        w.WriteI32(actor.Side == BattleSide.B ? 1 : 2);
        w.WriteI32(skillId);                    // ✅ skill (0 = ataque básico)
        w.WriteI32(0);
    }

    private void WriteTeam(PacketWriter w, IReadOnlyList<Combatant> team)
    {
        for (int i = 0; i < Battle.MaxPerSide; i++)
        {
            if (i >= team.Count)
            {
                w.WriteZeros(EntryLength);
                continue;
            }
            var c = team[i];
            var d = c.Digimon;
            d.Serial.Write(w);                  // 0
            w.WriteI32(d.Hp);                   // 16 HP depois do golpe
            w.WriteI32(d.Vp);                   // 20
            w.WriteI32(d.Evp);                  // 24
            w.WriteI32(c.Gauge);                // 28 barra de ação
            w.WriteI32(0);                      // 32
            w.WriteU8(c == actor ? attackType : (byte)0); // 36 ✅ quem age: 1 corpo-a-corpo/básico, 2 distância
            w.WriteU8(targetHits.TryGetValue(c, out bool landed) ? (byte)(landed ? 1 : 3) : (byte)0); // 37 ✅ alvo: 1 acertou, 3 errou
            w.WriteZeros(2);                    // 38..39
            if (activatedCards is not null && activatedCards.TryGetValue(c, out var cards))
            {
                // 40..63 ✅ Card Slash (conn=428, 433, 446): +40/+44/+48 CardId, +52/+56/+60 quantidade restante
                w.WriteU32(cards[0]?.CardId ?? 0);
                w.WriteU32(cards[1]?.CardId ?? 0);
                w.WriteU32(cards[2]?.CardId ?? 0);
                w.WriteU32(cards[0]?.RemainingAmount ?? 0);
                w.WriteU32(cards[1]?.RemainingAmount ?? 0);
                w.WriteU32(cards[2]?.RemainingAmount ?? 0);
            }
            else
            {
                w.WriteZeros(24);
            }
            w.WriteZeros(70);                   // 64..133 ❓ triggers de passivas/efeitos (DigimonRPG.bin @ 0xAF6350)
            w.WriteU16((ushort)c.Actions);      // 134 ações feitas na batalha
        }
    }
}

/// <summary>0x64 (720 bytes): ✅ digimon do time invocado: u8 2, u8 posição no time A, u16 0, u32 0, bloco, 16 zeros.</summary>
public sealed class BattleSummonPacket(int position, Digimon digimon) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.BattleSummon;

    protected override void Write(PacketWriter w)
    {
        w.WriteU8(2);
        w.WriteU8((byte)position);
        w.WriteU16(0);
        w.WriteU32(0);
        DigimonStatusBlock.Write(w, digimon);
        w.WriteZeros(16);
    }
}

/// <summary>0x65: digimon que fugiu da batalha (serial).</summary>
public sealed class FleeDigimonPacket(Serial digimon) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.BattleFleeDigimon;
    protected override void Write(PacketWriter w) => digimon.Write(w);
}

/// <summary>0x6A: tamer que fugiu da batalha (serial).</summary>
public sealed class FleeTamerPacket(Serial tamer) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.BattleFleeTamer;
    protected override void Write(PacketWriter w) => tamer.Write(w);
}

/// <summary>0x6D: fim da batalha. ✅ 1 vitória, 2 derrota (fuga conta como derrota: título "First Lose").</summary>
public sealed class BattleEndPacket(BattleResult result) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.BattleEnd;
    protected override void Write(PacketWriter w) => w.WriteU8((byte)result);
}

/// <summary>0x6E: EXP e bits ganhos por um inimigo derrotado (um por inimigo, na hora do golpe).</summary>
public sealed class RewardBitExpPacket(Serial digimon, long exp, double bits) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.BattleRewardBitExp;

    protected override void Write(PacketWriter w)
    {
        digimon.Write(w);
        w.WriteI64(exp);
        w.WriteF64(bits);
    }
}

/// <summary>0x70: subiu de nível (bloco de status completo). ✅ HP e VP enchem; o 0x67 seguinte já traz o HP novo.</summary>
public sealed class LevelUpPacket(Digimon digimon) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.BattleLevelUp;
    protected override void Write(PacketWriter w) => DigimonStatusBlock.Write(w, digimon);
}

/// <summary>0x84: bloco de status do digimon no fim da batalha (antes do 0x6D).</summary>
public sealed class BattleDigimonStatusPacket(Digimon digimon) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.BattleDigimonStatus;
    protected override void Write(PacketWriter w) => DigimonStatusBlock.Write(w, digimon);
}

/// <summary>
/// 0xB1 (1252 bytes): nível, EXP e equipamentos do tamer (fim de batalha e equipar/desequipar).
/// ✅ Layout confirmado em <c>DigimonRPG.bin @ 0x00B5C170</c> e <c>captures.db (conn=234/428)</c>:
/// 0..23 cabeçalho (u16 1, u16 nível, u32 0, i32 EXP, i32 EXP próx., 8 zeros), seguido de 11 registros de
/// equipamento de 108 B (Crests 4..6, Digimentals 7..9, D-ark 12, Spirits 26..28 e D-ark Card 25).
/// </summary>
public sealed class TamerExpPacket(Character character) : ServerPacket
{
    public const int TotalLength = 1252;

    public override ushort Opcode => (ushort)MapOpcode.TamerExp;

    protected override void Write(PacketWriter w)
    {
        w.WriteU16(1);
        w.WriteU16(character.Level);
        w.WriteU32(0);
        w.WriteI32(0);                          // EXP do tamer ❓ não persistido (igual ao 0x02)
        w.WriteI32((int)Game.Stats.ExpFormulas.TamerExpToNext(character.Level));
        w.WriteZeros(8);                        // 16..23

        int cursor = 24;
        foreach (var (place, offset) in EquipmentSlots.TamerRefreshSlots)
        {
            if (offset > cursor)
                w.WriteZeros(offset - cursor);
            if (character.Inventory.EquippedAt(place) is { } eq)
                ItemRecord.Write(w, eq);
            else
                w.WriteZeros(ItemRecord.Length);
            cursor = offset + ItemRecord.Length;
        }
        if (TotalLength > cursor)
            w.WriteZeros(TotalLength - cursor);
    }
}
