using System.Text.RegularExpressions;
using DRPGServer.Game;
using DRPGServer.Game.Entities;
using DRPGServer.Network.Packets.Map;

namespace DRPGServer.Network.Handlers;

/// <summary>
/// 0x71: distribuir ponto. O ponto vai para a forma daquele estágio (a janela de evolução deixa distribuir nas formas
/// liberadas).
/// </summary>
[PacketHandler(MapOpcode.DigimonAddAbilityPoint)]
[RequiresState(PlayerStates.Idle)]
public sealed class AddAbilityPointHandler : PlayerHandler<AddAbilityPointRequest>
{
    protected override void Handle(Player player, AddAbilityPointRequest request)
    {
        if (request.Stat is < 1 or > 4 || player.Character.FindDigimon(request.DigimonUid) is not { } digimon) return;
        if (digimon.FormAtStage(request.Stage) is not { } form) return;
        if (digimon.AllocatePoint(form, (StatKind)request.Stat))
        {
            player.MarkDirty();
            player.Send(new AddAbilityPointPacket(digimon, form, request.Stage, request.Stat));
        }
    }
}

/// <summary>0x72: subir skill (1 ou 2). Gasta um ponto de skill.</summary>
[PacketHandler(MapOpcode.DigimonSkillUpgrade)]
[RequiresState(PlayerStates.Idle)]
public sealed class SkillUpgradeHandler : PlayerHandler<SkillUpgradeRequest>
{
    protected override void Handle(Player player, SkillUpgradeRequest request)
    {
        if (request.Skill is < 1 or > DigimonForm.SkillSlots || player.Character.FindDigimon(request.DigimonUid) is not { } digimon) return;
        if (digimon.FormAtStage(request.Stage) is not { } form) return;
        if (digimon.UpgradeSkill(form, request.Skill - 1))
        {
            player.MarkDirty();
            player.Send(new SkillUpgradePacket(digimon, form, request.Stage, request.Skill));
        }
    }
}

/// <summary>0x82: janela de evolução (estágio 0 = atual). Responde o bloco da forma.</summary>
[PacketHandler(MapOpcode.DigimonFormInfo)]
[RequiresState(PlayerStates.Any)]
public sealed class DigimonFormInfoHandler : PlayerHandler<DigimonFormInfoRequest>
{
    protected override void Handle(Player player, DigimonFormInfoRequest request)
    {
        if (request.Stage > byte.MaxValue || player.Character.FindDigimon(request.DigimonUid) is not { } digimon) return;
        if (digimon.FormAtStage((byte)request.Stage) is not { } form) return;
        player.Send(new DigimonFormInfoPacket(request.Stage, digimon, form));
    }
}

/// <summary>0x97: abrir a janela de títulos do digimon.</summary>
[PacketHandler(MapOpcode.DigimonTitleList)]
[RequiresState(PlayerStates.Any)]
public sealed class DigimonTitleListHandler : PlayerHandler<DigimonTitleListRequest>
{
    protected override void Handle(Player player, DigimonTitleListRequest request)
    {
        if (request.Action != 2) return;
        if (player.Character.FindDigimon(request.DigimonUid) is not { } digimon)
        {
            player.Send(new DigimonTitleListPacket(request.DigimonUid, [], success: false));
            return;
        }
        player.Send(new DigimonTitleListPacket(digimon.Uid, digimon.Titles));
    }
}

/// <summary>0x99: equipar/desequipar (título 0) o título do digimon.</summary>
[PacketHandler(MapOpcode.DigimonTitleEquip)]
[RequiresState(PlayerStates.Idle)]
public sealed class DigimonTitleEquipHandler : PlayerHandler<DigimonTitleEquipRequest>
{
    protected override void Handle(Player player, DigimonTitleEquipRequest request)
    {
        var (action, digimonUid, titleId) = request;
        if (action != 2) return;

        if (player.Character.FindDigimon(digimonUid) is not { } digimon || !digimon.EquipTitle(titleId))
        {
            player.Send(new DigimonTitleEquipPacket(digimonUid, titleId, success: false));
            return;
        }

        player.RequestImmediateSave();
        // ✅ Ordem confirmada em conn=76/141: 0xB4 → 0x10 (se líder) → 0x99
        player.Send(new SingleDigimonInfoPacket(digimon));
        if (digimon == player.Leader)
            player.Zone?.Broadcast(new RefreshCharacterInfoPacket(player.Character));
        player.Send(new DigimonTitleEquipPacket(digimon.Uid, titleId));
    }
}

/// <summary>0x9E: formas para evoluir agora (só em batalha).</summary>
[PacketHandler(MapOpcode.EvolutionList)]
[RequiresState(PlayerStates.InBattle)]
public sealed class EvolutionListHandler : PlayerHandler<EvolutionListRequest>
{
    protected override void Handle(Player player, EvolutionListRequest request) =>
        player.Send(new EvolutionListPacket(player.Battle!.EvolutionOptions(player, request.DigimonUid)));
}

/// <summary>0xF1: o digimon vira líder (troca com a posição 0).</summary>
[PacketHandler(MapOpcode.SetLeader)]
[RequiresState(PlayerStates.Idle)]
public sealed class SetLeaderHandler : PlayerHandler<SetLeaderRequest>
{
    protected override void Handle(Player player, SetLeaderRequest request)
    {
        var c = player.Character;
        if (request.CharacterUid != c.Uid || c.FindDigimon(request.DigimonUid) is not { } digimon) return;
        if (c.SetLeader(digimon))
        {
            player.MarkDirty();
            player.Send(new SetLeaderPacket(c.Uid, digimon.Uid));
        }
    }
}

/// <summary>0x173: o botão k (0..2) troca as posições k + 1 e k + 2 do time.</summary>
[PacketHandler(MapOpcode.PartySwap)]
[RequiresState(PlayerStates.Idle)]
public sealed class PartySwapHandler : PlayerHandler<PartySwapRequest>
{
    protected override void Handle(Player player, PartySwapRequest request)
    {
        var button = request.Button;
        if (button > Character.PartySize - 3) return;
        if (player.Character.SwapSlots(button + 1, button + 2))
        {
            player.MarkDirty();
            player.Send(new PartySwapPacket(button));
        }
    }
}

/// <summary>
/// 0xF0: renomear; sem nome é consulta. Digimon: uma renomeação grátis para quem nasceu "InputName"; o nome segue a
/// regra da criação (letras e números). ❓ Renomeação paga (item) ainda não; tamer sempre 2, como o oficial na entrada.
/// </summary>
[PacketHandler(MapOpcode.Rename)]
[RequiresState(PlayerStates.Any)]
public sealed partial class RenameHandler : PlayerHandler<RenameRequest>
{
    [GeneratedRegex("^[a-zA-Z0-9]{1,19}$")]
    private static partial Regex ValidName();

    protected override void Handle(Player player, RenameRequest request)
    {
        var (kind, uid, name) = request;
        if (kind != RenameKind.Digimon)
        {
            player.Send(NotAvailable(kind, uid));
            return;
        }

        var digimon = player.Character.FindDigimon(uid);
        bool free = digimon is { FreeRename: true } && !player.InBattle;
        if (name.Length == 0 || !free || !ValidName().IsMatch(name))
        {
            player.Send(free && name.Length == 0
                ? new RenamePacket(RenameResult.Free, RenameKind.Digimon, uid, "")
                : NotAvailable(RenameKind.Digimon, uid));
            return;
        }
        digimon!.Name = name;
        digimon.FreeRename = false;
        player.RequestImmediateSave();
        player.Send(new RenamePacket(RenameResult.Renamed, RenameKind.Digimon, uid, name));
    }

    /// <summary>O tipo 1 chega logo na entrada, às vezes antes de o personagem estar no mundo: responde sem derrubar.</summary>
    protected override void OnNotInWorld(Session session, RenameRequest request) =>
        session.Send(NotAvailable(request.Kind, request.Uid));

    private static RenamePacket NotAvailable(RenameKind kind, uint uid) => new(RenameResult.NotAvailable, kind, uid, "");
}

/// <summary>
/// 0xB4: ✅ o cliente manda um por digimon 6,6 s depois do 0x6D, ao sair da tela de resultado; o oficial ecoa o bloco
/// de cada um com os dados dele.
/// </summary>
[PacketHandler(MapOpcode.SingleDigimonInfo)]
[RequiresState(PlayerStates.Any)]
public sealed class DigimonInfoHandler : PlayerHandler<DigimonInfoRequest>
{
    protected override void Handle(Player player, DigimonInfoRequest request)
    {
        if (player.Character.FindDigimon(request.Digimon) is { } digimon)
            player.Send(new SingleDigimonInfoPacket(digimon));
    }
}
