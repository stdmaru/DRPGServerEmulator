using System.Text;
using DRPG.Server.Game;
using DRPG.Server.Game.Entities;
using DRPG.Server.Game.Items;
using DRPG.Server.Game.Npcs;
using DRPG.Server.Network;
using DRPG.Server.Network.Packets.Map;
using DRPG.Shared.Protocol;

namespace DRPG.Server.Tests;

public class AdminDebugChatTests
{
    private static (Player Player, Session Session) NewPlayer(Authority authority, byte mapId = 79)
    {
        var session = TestSupport.NewSession();
        var player = new Player(session, new AuthenticatedAccount(1, "user", authority), Entities.Character(mapId));
        session.Player = player;
        return (player, session);
    }

    private static (string Sender, string Message) DecodeChat(byte[] frame)
    {
        var payload = frame.AsSpan(PacketHeader.Length);
        Assert.Equal(277, payload.Length);
        string sender = Encoding.ASCII.GetString(payload[..ChatNormalPacket.NicknameLength]).TrimEnd('\0');
        string message = Encoding.ASCII.GetString(payload[ChatNormalPacket.NicknameLength..]).TrimEnd('\0');
        return (sender, message);
    }

    [Fact]
    public void SendDebug_sends_0x22_only_to_administrators()
    {
        var (admin, adminSession) = NewPlayer(Authority.Administrator);
        var (gm, gmSession) = NewPlayer(Authority.GameMaster);
        var (normal, normalSession) = NewPlayer(Authority.None);

        Assert.True(admin.IsAdmin);
        Assert.False(gm.IsAdmin);
        Assert.False(normal.IsAdmin);

        admin.SendDebug("NPC Choice ID 999 not registered");
        gm.SendDebug("ignored");
        normal.SendDebug("ignored");

        var frame = Assert.Single(adminSession.DrainOutgoing());
        Assert.Equal((ushort)MapOpcode.ChatNotice, PacketHeader.Read(frame).Opcode);
        Assert.Equal(("[DEBUG]", "NPC Choice ID 999 not registered"), DecodeChat(frame));

        Assert.Empty(gmSession.DrainOutgoing());
        Assert.Empty(normalSession.DrainOutgoing());
    }

    [Fact]
    public void NpcScriptRegistry_echoes_unregistered_and_wrong_map_choices_to_admin_only()
    {
        var script = new TestNpcScript(mapId: 40, choiceId: 1234);
        var registry = TestSupport.NewNpcRegistry(script);

        var (admin, adminSession) = NewPlayer(Authority.Administrator, mapId: 79);
        var (normal, normalSession) = NewPlayer(Authority.None, mapId: 79);

        // Escolha não registrada
        Assert.False(registry.Choose(admin, 9999));
        Assert.False(registry.Choose(normal, 9999));

        var unregisteredFrame = Assert.Single(adminSession.DrainOutgoing());
        Assert.Equal((ushort)MapOpcode.ChatNotice, PacketHeader.Read(unregisteredFrame).Opcode);
        var (sender1, msg1) = DecodeChat(unregisteredFrame);
        Assert.Equal("[DEBUG]", sender1);
        Assert.Contains("9999", msg1);
        Assert.Contains("not registered", msg1);
        Assert.Empty(normalSession.DrainOutgoing());

        // Escolha registrada em outro mapa (espera 40, jogador está em 79)
        Assert.False(registry.Choose(admin, 1234));
        var wrongMapFrame = Assert.Single(adminSession.DrainOutgoing());
        var (_, msg2) = DecodeChat(wrongMapFrame);
        Assert.Contains("1234", msg2);
        Assert.Contains("NPC 40:12", msg2);
    }

    private sealed class TestNpcScript : NpcScriptBase
    {
        public TestNpcScript(byte mapId, uint choiceId) : base(TestSupport.Items())
        {
            MapId = mapId;
            RegisterChoice(choiceId, 12, _ => true);   // NPC 40:12 do pacote de teste
        }

        public override byte MapId { get; }
    }
}
