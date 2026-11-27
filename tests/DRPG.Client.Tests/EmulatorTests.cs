using DRPG.Client.Net;
using DRPG.Client.Scenes;
using DRPG.Client.Ui;
using DRPG.Shared.Packets.Channel;

namespace DRPG.Client.Tests;

/// <summary>Skipped unless <c>DRPG_E2E_USER</c> and <c>DRPG_E2E_KEY</c> point to an account of a running emulator.</summary>
public sealed class EmulatorFactAttribute : FactAttribute
{
    public EmulatorFactAttribute()
    {
        if (EmulatorEnvironment.User is null || EmulatorEnvironment.Key is null)
            Skip = "set DRPG_E2E_USER and DRPG_E2E_KEY (docs/arquitetura/cliente.md#teste-de-ponta-a-ponta)";
    }
}

internal static class EmulatorEnvironment
{
    public static string? User => Environment.GetEnvironmentVariable("DRPG_E2E_USER");

    public static string? Key => Environment.GetEnvironmentVariable("DRPG_E2E_KEY");

    /// <summary>The emulator of the devcontainer unless <c>DRPG_E2E_LOGIN</c> / <c>DRPG_E2E_CHANNEL</c> say otherwise.</summary>
    public static ClientSettings Settings => new(
        ServerAddress.Parse(Environment.GetEnvironmentVariable("DRPG_E2E_LOGIN")) ?? ClientSettings.Local.Login,
        ServerAddress.Parse(Environment.GetEnvironmentVariable("DRPG_E2E_CHANNEL")) ?? ClientSettings.Local.Channel);
}

/// <summary>
/// End to end against the real emulator (not in CI: it needs the server and a database): the client opens with the
/// user and the key of the launcher and receives the character list.
/// </summary>
public sealed class EmulatorTests
{
    private static void Pump(AccountSession session, ref ulong now, Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!done() && DateTime.UtcNow < deadline)
        {
            now += 33;
            session.Update(now);
            Thread.Sleep(5);
        }
    }

    [EmulatorFact]
    public void The_launcher_key_opens_the_character_list()
    {
        var log = new List<string>();
        using var session = new AccountSession(EmulatorEnvironment.Settings, new GameCommandLine(EmulatorEnvironment.User!, EmulatorEnvironment.Key!), log.Add);
        ulong now = 0;

        // the calls of the scenes: 0x05, then a channel click, 0x04 on its reply, 0x12 + 0x14 in the character selection
        session.StartLogin(now);
        Pump(session, ref now, () => session.Step is AccountStep.LoggedIn or AccountStep.Failed);
        session.SelectChannel(now);
        Pump(session, ref now, () => session.Step is AccountStep.ChannelSelected or AccountStep.Failed);
        session.Authenticate(now);
        Pump(session, ref now, () => session.Step is AccountStep.Authenticated or AccountStep.Failed);
        session.RequestCharacterList(now);
        Pump(session, ref now, () => session.Step == AccountStep.Ready && session.UserConfirmed);

        Console.WriteLine(string.Join(Environment.NewLine, log));
        Assert.Null(session.FailureMessage);
        Assert.Equal(AccountStep.Ready, session.Step);
        Assert.True(session.UserConfirmed);
        Assert.Equal(4, session.Characters!.Slots.Count);
    }

    [EmulatorFact]
    public void A_wrong_key_is_refused()
    {
        var log = new List<string>();
        using var session = new AccountSession(EmulatorEnvironment.Settings, new GameCommandLine(EmulatorEnvironment.User!, new string('0', 40)), log.Add);
        ulong now = 0;

        session.StartLogin(now);
        Pump(session, ref now, () => session.Step is AccountStep.LoggedIn or AccountStep.Failed);
        session.SelectChannel(now);
        Pump(session, ref now, () => session.Step is AccountStep.ChannelSelected or AccountStep.Failed);
        session.Authenticate(now);
        Pump(session, ref now, () => session.AuthenticationRefusal is not null || session.Step == AccountStep.Failed);

        Assert.NotNull(session.AuthenticationRefusal);
        Assert.Null(session.Characters);
    }

    [EmulatorFact]
    public void Creating_and_deleting_a_character_updates_the_character_list()
    {
        var log = new List<string>();
        using var session = new AccountSession(EmulatorEnvironment.Settings, new GameCommandLine(EmulatorEnvironment.User!, EmulatorEnvironment.Key!), log.Add);
        ulong now = 0;

        session.StartLogin(now);
        Pump(session, ref now, () => session.Step is AccountStep.LoggedIn or AccountStep.Failed);
        session.SelectChannel(now);
        Pump(session, ref now, () => session.Step is AccountStep.ChannelSelected or AccountStep.Failed);
        session.Authenticate(now);
        Pump(session, ref now, () => session.Step is AccountStep.Authenticated or AccountStep.Failed);
        session.RequestCharacterList(now);
        Pump(session, ref now, () => session.Step == AccountStep.Ready && session.UserConfirmed);

        Assert.Equal(AccountStep.Ready, session.Step);
        int targetSlot = CharacterNameRules.PickTargetSlot(session.Characters!.Slots, selectedSlot: -1);
        Assert.True(targetSlot >= 0, "E2E account has no empty character slot");

        string name = $"E2e{Random.Shared.Next(100000, 999999)}";
        int beforeCreate = session.CharacterListVersion;
        var header = CharacterNameRules.BuildCreateHeader((byte)targetSlot, Random.Shared);
        session.CreateCharacter(new CharacterCreateRequest(header, TamerId: 1, name, DigimonId: 10, DigimonName: "Gigimon"));
        Pump(session, ref now, () => session.CharacterListVersion > beforeCreate || session.LastCreateError is not null || session.Step == AccountStep.Failed);

        Assert.Null(session.FailureMessage);
        Assert.Null(session.LastCreateError);
        var created = session.Characters!.Slots.FirstOrDefault(s => !s.IsEmpty && s.Name == name);
        Assert.NotNull(created);

        int beforeDelete = session.CharacterListVersion;
        session.DeleteCharacter(created!.Uid, DeleteCharacterDialog.ComputePasswordMd5(""));
        Pump(session, ref now, () => session.CharacterListVersion > beforeDelete || session.LastDeleteError is not null || session.Step == AccountStep.Failed);

        Assert.Null(session.FailureMessage);
        Assert.Null(session.LastDeleteError);
        Assert.DoesNotContain(session.Characters!.Slots, s => !s.IsEmpty && s.Uid == created.Uid);
    }
}
