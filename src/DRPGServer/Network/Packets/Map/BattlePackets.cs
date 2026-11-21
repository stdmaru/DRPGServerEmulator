using DRPGServer.Game;
using DRPGServer.Game.Battle;
using DRPGServer.Game.Entities;
using DRPGServer.Network.Protocol;

namespace DRPGServer.Network.Packets.Map;

/// <summary>0x62: batalha criada, com o serial que o cliente confirma de volta.</summary>
public sealed class StartBattlePacket(Serial battle) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.BattleStartRes;

    protected override void Write(PacketWriter w)
    {
        battle.Write(w);
        w.WriteU8(1);                           // ?
        w.WriteU8(2);                           // ?
        w.WriteU16(0);
    }
}

/// <summary>0x63: os dois times (B primeiro, depois A), cinco posições de 688 bytes cada.</summary>
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

/// <summary>0x69: o servidor aceita o "pronto" do cliente. Vazio.</summary>
public sealed class BattleBeginPacket : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.BattleBegin;
    protected override void Write(PacketWriter w) { }
}

/// <summary>0x6B: valor atual da barra de ação de um digimon.</summary>
public sealed class ActionCooldownPacket(Serial digimon, int gauge) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.BattleActionCooldown;

    protected override void Write(PacketWriter w)
    {
        digimon.Write(w);
        w.WriteI32(gauge);
        w.WriteI32(0);                          // ?
    }
}

/// <summary>
/// 0x35: ação escolhida. Action: 0 limpa, 1 atacar, 2 fugir, 3 zerar barra, 5 cancelar invocação (❓ protótipo).
/// </summary>
public sealed class ActionRequestPacket(byte action, Serial requester, Serial target) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.BattleActionReq;

    protected override void Write(PacketWriter w)
    {
        w.WriteU8(action);
        w.WriteU16(0x0A);                       // ícone: ataque = 10, fuga = 4
        w.WriteU8(0);
        requester.Write(w);
        target.Write(w);
    }
}

/// <summary>0x68: trava as barras durante a animação. Vazio.</summary>
public sealed class LockActionBarPacket : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.BattleLockActionBar;
    protected override void Write(PacketWriter w) { }
}

/// <summary>0x67: resultado de uma ação. Time de quem ataca primeiro, 136 bytes por posição.</summary>
public sealed class ActionResultsPacket(
    IReadOnlyList<Digimon> attackers, IReadOnlyList<Digimon> defenders, Digimon attacker, Digimon target, bool enemyTurn) : ServerPacket
{
    private const int EntryLength = 136;

    public override ushort Opcode => (ushort)MapOpcode.BattleActionResults;

    protected override void Write(PacketWriter w)
    {
        WriteTeam(w, attackers);
        WriteTeam(w, defenders);
        w.WriteI32(enemyTurn ? 1 : 2);          // 1 inimigo ataca, 2 jogador ataca
        w.WriteZeros(8);
    }

    private void WriteTeam(PacketWriter w, IReadOnlyList<Digimon> team)
    {
        for (int i = 0; i < Battle.MaxPerSide; i++)
        {
            if (i >= team.Count)
            {
                w.WriteZeros(EntryLength);
                continue;
            }
            var d = team[i];
            d.Serial.Write(w);
            w.WriteI32(d.Hp);
            w.WriteI32(d.Vp);
            w.WriteI32(d.Evp);
            w.WriteBytes([0x87, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]); // ?
            w.WriteBool(d == attacker);
            w.WriteBool(d == target);
            w.WriteSparse(98, (96, [0x01]));    // ?
        }
    }
}

/// <summary>0x6D: fim da batalha.</summary>
public sealed class BattleEndPacket(BattleResult result) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.BattleEnd;
    protected override void Write(PacketWriter w) => w.WriteU8((byte)result);
}

/// <summary>0x6E: EXP e bits ganhos.</summary>
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

/// <summary>0x70: subiu de nível (bloco de status completo).</summary>
public sealed class LevelUpPacket(Digimon digimon) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.BattleLevelUp;
    protected override void Write(PacketWriter w) => DigimonStatusBlock.Write(w, digimon);
}
