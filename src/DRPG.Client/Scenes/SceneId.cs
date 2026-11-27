namespace DRPG.Client.Scenes;

/// <summary>
/// The scene numbers of <c>FUN_00b500a0(n)</c> (✅ v322): 1 = <c>CSceneTitle</c> (<c>0x00b54ae0</c>), 2 = <c>CSceneServer</c>
/// (<c>0x00b504b0</c>), 4 = <c>CSceneCharSel</c> (<c>0x00b43f20</c>), 5 = <c>CSceneGame</c> (<c>0x00b494f0</c>).
/// ❓ 3 is not created there.
/// </summary>
public enum SceneId
{
    Title = 1,
    Server = 2,
    CharacterSelect = 4,
    Game = 5,
}
