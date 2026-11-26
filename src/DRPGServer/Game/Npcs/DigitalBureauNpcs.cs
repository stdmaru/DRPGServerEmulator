using DRPGServer.Game.Entities;
using DRPGServer.Game.Items;

namespace DRPGServer.Game.Npcs;

/// <summary>
/// Mapa 1 — <c>Digital Administration Bureau</c> (NPCs de escolha <c>0x56</c>).
/// ✅ Escolha 10 (Nyang, NPC 33 — "Deliver the Tsunomon's Introduction Letter", capturada em S56 C121 e S62 C234):
/// recebe a carta do Tunomon (22008) e entrega Teleport X (907), Crest of Courage +5 (107) e Taming Net : Rookie X (10026).
/// Em falha ou repetição, o oficial responde <c>S2C 0x56 = 99</c> para fechar a janela <c>0x38</c>.
/// </summary>
public sealed class DigitalBureauNpcs : NpcScriptBase
{
    public const byte BureauMapId = 1;
    public const uint NyangIntroLetterChoice = 10;
    /// <summary>✅ Nyang (<c>npc_info_us</c> 1:33, (66, 86)).</summary>
    public const ushort NyangNpc = 33;

    public const uint TeleportXItemId = 907;
    public const uint BeginnerCrestItemId = 107;
    public const uint RookieNetItemId = 10026;

    public override byte MapId => BureauMapId;

    public DigitalBureauNpcs(ItemService items) : base(items)
    {
        RegisterChoice(NyangIntroLetterChoice, NyangNpc, DeliverIntroLetter);
    }

    private bool DeliverIntroLetter(Player player)
    {
        bool ok = ExchangeItemsOnce(
            player,
            NyangIntroLetterChoice,
            VillageOfBeginningNpcs.TunomonCard, 1,
            new ItemGrant(TeleportXItemId),
            new ItemGrant(BeginnerCrestItemId),
            new ItemGrant(RookieNetItemId));

        if (!ok) ReplyError(player);
        return ok;
    }
}
