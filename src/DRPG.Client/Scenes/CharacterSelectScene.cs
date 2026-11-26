using DRPG.Client.Net;
using DRPG.Engine.Platform;
using DRPG.Engine.Rendering;
using DRPG.Shared.Packets.Channel;

namespace DRPG.Client.Scenes;

/// <summary>
/// ❓ PROVISIONAL <c>CSceneCharSel</c> (docs/cliente/cenas.md#cscenecharsel): Phase C replaces it with the real slots
/// and buttons. It draws the background of the original (<c>dr2_ch_000_000</c>) and one line of text per slot with what
/// the list carries, and keeps the channel connection alive.
/// </summary>
public sealed class CharacterSelectScene(GameContext game) : IScene
{
    private readonly TextLine[] _lines = Enumerable.Range(0, CharacterListReply.SlotCount + 1).Select(_ => new TextLine(game.Font)).ToArray();
    private ITexture _background = null!; // set in Init

    public int FrameIntervalMs => 0;

    public void Init() => _background = game.Sprites.Gui("dr2_ch_000_000")[0];

    public void Handle(InputEvent input)
    {
    }

    public void Frame(IRenderer renderer, ulong nowMs)
    {
        var account = game.Account!; // the server scene only leaves with an account that is ready
        account.Update(nowMs);
        renderer.Draw(_background, 0, 0);
        game.DrawVersion(renderer, 1);
        _lines[0].Draw(renderer, account.Step == AccountStep.Failed ? account.FailureMessage ?? "" : "[provisional] character list", 20, 560);
        var slots = account.Characters?.Slots ?? [];
        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            var text = slot.IsEmpty ? $"slot {slot.Slot}: empty" : $"slot {slot.Slot}: {slot.Name} lv {slot.Level}  {slot.LeaderName} lv {slot.LeaderLevel}";
            _lines[i + 1].Draw(renderer, text, 20, 20 + i * 20);
        }
    }

    public void Dispose()
    {
        foreach (var line in _lines)
            line.Dispose();
    }
}
