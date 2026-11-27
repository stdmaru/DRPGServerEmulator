using DRPG.Engine.Audio;

namespace DRPG.Client.Scenes;

/// <summary>✅ The sound effects the game scene registers when it starts (<c>0x00b4bab0</c>, <c>0x00a187b0(id, file)</c>).</summary>
public static class GameSceneSounds
{
    public static readonly IReadOnlyList<(int Id, string File)> Effects =
    [
        (0, "sound\\walk01-1.wav"), (2, "sound\\be_impact2.wav"), (3, "sound\\be_impact1.wav"), (4, "sound\\be_attackmiss1.wav"),
        (5, "sound\\effect_slash.wav"), (6, "sound\\effect_evol.wav"), (7, "sound\\effect_put.wav"), (9, "sound\\052_s2.wav"),
        (10, "sound\\093_s2.wav"), (0xb, "Sound\\bt_win.wav"), (0xc, "Sound\\bt_lose.wav"), (0xd, "Sound\\BIT_Ringing.wav"),
        (0xe, "Sound\\w_on.wav"), (0xf, "Sound\\w_off.wav"), (0x10, "Sound\\effect_pickup.wav"), (0x11, "Sound\\be_captwin.wav"),
        (0x12, "Sound\\be_dgcapt.wav"), (0x13, "Sound\\Bit_spend.wav"), (0x14, "Sound\\be_attackmiss2.wav"), (0x15, "Sound\\be_levelup.wav"),
        (0x16, "Sound\\be_captlose.wav"), (0x17, "Sound\\ui_charge.wav"), (0x18, "Sound\\ds_beam1.wav"), (0x19, "Sound\\ds_beam2.wav"),
        (0x1a, "Sound\\ds_bubble.wav"), (0x1b, "Sound\\ds_missile.wav"), (0xf0, "Sound\\ds_explosion.wav"),
        (0xf1, "Sound\\Effect_Clockwork.wav"), (0xf2, "Sound\\Effect_Marching.wav"), (0xf3, "Sound\\Effect_Pang.wav"),
        (0xf4, "Sound\\ds_red_spark.wav"), (0xf5, "Sound\\B_LD_Count.wav"), (0x155, "Sound\\DM_DiceBounce.wav"),
        (0x156, "Sound\\DM_DiceSave.wav"), (0x158, "Sound\\DM_Getting.wav"), (0x157, "Sound\\DM_Fly.wav"), (0x159, "Sound\\DM_Move.wav"),
        (0x15a, "Sound\\DM_OneMore.wav"), (0x15b, "Sound\\DM_Role.wav"), (0x15c, "Sound\\DM_SandGlass.wav"), (0x312, "Sound\\Ec_Skill.wav"),
        (0x313, "Sound\\Jogress_Start.wav"), (0x314, "Sound\\Jogress_Success.wav"), (0x315, "Sound\\Jogress_Fail.wav"),
    ];

    public static void Register(SoundBank sounds)
    {
        foreach (var (id, file) in Effects)
            sounds.Register(id, file);
    }
}
