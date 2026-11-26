using DRPG.Client.Net;
using DRPG.Engine.Platform;
using DRPG.Engine.Rendering;

namespace DRPG.Client.Scenes;

/// <summary>
/// ❓ PROVISIONAL <c>CSceneServer</c> (world selection, docs/cliente/cenas.md#cserverscene): Phase C
/// replaces it with the real layout (DIGITAL WORLD / BATTLE ZONE buttons, sounds, messages). For now it only draws the
/// background and the version text (✅ x = 1, <c>0x00b54240</c>), connects as soon as it starts, runs the login →
/// channel sequence and moves on to the character selection when the list arrives. Escape quits on a failure.
/// </summary>
public sealed class ServerScene(GameContext game, Action<string> log) : IScene
{
    private readonly TextLine _status = new(game.Font);
    private ITexture _background = null!; // set in Init
    private bool _started;

    public int FrameIntervalMs => 0;

    public void Init()
    {
        _background = game.Sprites.Gui("dr2_in_000_000")[0];
        game.Account = new AccountSession(game.Settings, game.Command, log);
    }

    public void Handle(InputEvent input)
    {
        if (input is KeyEvent { Key: GameKey.Escape } && game.Account is { Step: AccountStep.Failed })
            game.FatalError = game.Account.FailureMessage;
    }

    public void Frame(IRenderer renderer, ulong nowMs)
    {
        var account = game.Account!; // created in Init
        if (!_started)
        {
            _started = true;
            account.Start(nowMs);
        }
        account.Update(nowMs);
        renderer.Draw(_background, 0, 0);
        game.DrawVersion(renderer, 1);
        _status.Draw(renderer, account.Step == AccountStep.Failed ? $"{account.FailureMessage} (Esc to quit)" : $"[provisional] {account.Step}...", 20, 560);
        if (account.Step == AccountStep.Ready)
            game.Scenes.Switch(SceneId.CharacterSelect);
    }

    public void Dispose() => _status.Dispose();
}
