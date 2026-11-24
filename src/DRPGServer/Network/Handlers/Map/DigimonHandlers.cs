using DRPGServer.Game;
using DRPGServer.Game.Entities;
using DRPGServer.Game.World;
using DRPGServer.Network.Packets.Map;
using DRPGServer.Network.Protocol;

namespace DRPGServer.Network.Handlers;

/// <summary>
/// 0x71 (C2S 718): ✅ bloco de status ecoado (uid em 0, serial em 4), u8 estágio da forma e u8 atributo (1..4).
/// O ponto vai para a forma daquele estágio (a janela de evolução deixa distribuir nas formas liberadas).
/// </summary>
[PacketHandler(MapOpcode.DigimonAddAbilityPoint)]
public sealed class AddAbilityPointHandler : PlayerHandler
{
    protected override void Handle(Player player, PacketReader packet)
    {
        uint uid = packet.ReadU32();
        packet.Skip(DigimonStatusBlock.Length - 4);
        var stage = packet.ReadU8();
        var stat = packet.ReadU8();

        if (player.InBattle || player.Character.FindDigimon(uid) is not { } digimon || stat is < 1 or > 4) return;
        if (digimon.FormAtStage(stage) is not { } form) return;
        if (digimon.AllocatePoint(form, (StatKind)stat))
        {
            player.MarkDirty();
            player.Send(new AddAbilityPointPacket(digimon, form, stage, stat));
        }
    }
}

/// <summary>0x72 (C2S 718): ✅ bloco ecoado, u8 estágio da forma e u8 skill (1 ou 2). Gasta um ponto de skill.</summary>
[PacketHandler(MapOpcode.DigimonSkillUpgrade)]
public sealed class SkillUpgradeHandler : PlayerHandler
{
    protected override void Handle(Player player, PacketReader packet)
    {
        uint uid = packet.ReadU32();
        packet.Skip(DigimonStatusBlock.Length - 4);
        var stage = packet.ReadU8();
        var skill = packet.ReadU8();

        if (player.InBattle || player.Character.FindDigimon(uid) is not { } digimon || skill is < 1 or > DigimonForm.SkillSlots) return;
        if (digimon.FormAtStage(stage) is not { } form) return;
        if (digimon.UpgradeSkill(form, skill - 1))
        {
            player.MarkDirty();
            player.Send(new SkillUpgradePacket(digimon, form, stage, skill));
        }
    }
}

/// <summary>0x82 (C2S 734): ✅ janela de evolução: u32 estágio (0 = atual), u32 0, bloco ecoado. Responde o bloco da forma.</summary>
[PacketHandler(MapOpcode.DigimonFormInfo)]
public sealed class DigimonFormInfoHandler : PlayerHandler
{
    protected override void Handle(Player player, PacketReader packet)
    {
        uint stage = packet.ReadU32();
        packet.ReadU32();
        uint uid = packet.ReadU32();
        if (stage > byte.MaxValue || player.Character.FindDigimon(uid) is not { } digimon) return;
        if (digimon.FormAtStage((byte)stage) is not { } form) return;
        player.Send(new DigimonFormInfoPacket(stage, digimon, form));
    }
}

/// <summary>0x9E (C2S 18): ✅ u32 uid do digimon; o servidor responde as formas para evoluir agora (só em batalha).</summary>
[PacketHandler(MapOpcode.EvolutionList)]
public sealed class EvolutionListHandler : PlayerHandler
{
    protected override void Handle(Player player, PacketReader packet)
    {
        uint uid = packet.ReadU32();
        if (player.Battle is { } battle)
            player.Send(new EvolutionListPacket(battle.EvolutionOptions(player, uid)));
    }
}

/// <summary>0xF1 (C2S 26): ✅ u32 0, u32 personagem, u32 digimon: o digimon vira líder (troca com a posição 0).</summary>
[PacketHandler(MapOpcode.SetLeader)]
public sealed class SetLeaderHandler : PlayerHandler
{
    protected override void Handle(Player player, PacketReader packet)
    {
        packet.ReadU32();
        uint characterUid = packet.ReadU32();
        uint digimonUid = packet.ReadU32();
        var c = player.Character;
        if (player.InBattle || characterUid != c.Uid || c.FindDigimon(digimonUid) is not { } digimon) return;
        if (c.SetLeader(digimon))
        {
            player.MarkDirty();
            player.Send(new SetLeaderPacket(c.Uid, digimon.Uid));
        }
    }
}

/// <summary>0x173 (C2S 22): ✅ u16 0, u16 botão k (0..2): troca as posições k + 1 e k + 2 do time.</summary>
[PacketHandler(MapOpcode.PartySwap)]
public sealed class PartySwapHandler : PlayerHandler
{
    protected override void Handle(Player player, PacketReader packet)
    {
        packet.ReadU16();
        ushort button = packet.ReadU16();
        if (player.InBattle || button > Character.PartySize - 3) return;
        if (player.Character.SwapSlots(button + 1, button + 2))
        {
            player.MarkDirty();
            player.Send(new PartySwapPacket(button));
        }
    }
}

/// <summary>
/// 0xF0 (C2S 42): ✅ renomear. u8 0, u8 tipo (1 tamer, 2 digimon), u16 0, u32 uid, nome[20]. Sem nome é consulta.
/// Digimon: uma renomeação grátis para quem nasceu "InputName"; o nome segue a regra da criação (letras e números).
/// ❓ Renomeação paga (item) ainda não; tamer sempre 2, como o oficial na entrada.
/// </summary>
[PacketHandler(MapOpcode.Rename)]
public sealed partial class RenameHandler : IWorldPacketHandler
{
    [System.Text.RegularExpressions.GeneratedRegex("^[a-zA-Z0-9]{1,19}$")]
    private static partial System.Text.RegularExpressions.Regex ValidName();

    public void Handle(Session session, PacketReader packet)
    {
        packet.ReadU8();
        var kind = (RenameKind)packet.ReadU8();
        packet.ReadU16();
        uint uid = packet.ReadU32();
        var name = packet.ReadFixedString(RenamePacket.NameLength);
        // o tipo 1 chega logo na entrada, às vezes antes de o personagem estar no mundo: não derruba a conexão
        if (kind != RenameKind.Digimon || session.Player is not { } player)
        {
            session.Send(new RenamePacket(RenameResult.NotAvailable, kind, uid, ""));
            return;
        }

        var digimon = player.Character.FindDigimon(uid);
        bool free = digimon is { FreeRename: true } && !player.InBattle;
        if (name.Length == 0 || !free || !ValidName().IsMatch(name))
        {
            player.Send(new RenamePacket(free && name.Length == 0 ? RenameResult.Free : RenameResult.NotAvailable,
                RenameKind.Digimon, uid, ""));
            return;
        }
        digimon!.Name = name;
        digimon.FreeRename = false;
        player.RequestImmediateSave();
        player.Send(new RenamePacket(RenameResult.Renamed, RenameKind.Digimon, uid, name));
    }
}

/// <summary>
/// 0xB4 (C2S, bloco de status): ✅ o cliente manda um por digimon 6,6 s depois do 0x6D, ao sair da tela de
/// resultado; o oficial ecoa o bloco de cada um com os dados dele.
/// </summary>
[PacketHandler(MapOpcode.SingleDigimonInfo)]
public sealed class DigimonInfoHandler : PlayerHandler
{
    protected override void Handle(Player player, PacketReader packet)
    {
        packet.ReadU32();
        var serial = Serial.Read(packet);
        if (player.Character.FindDigimon(serial) is { } digimon)
            player.Send(new SingleDigimonInfoPacket(digimon));
    }
}
