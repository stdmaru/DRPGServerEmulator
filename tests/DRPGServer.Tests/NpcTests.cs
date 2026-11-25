using System.Buffers.Binary;
using DRPGServer.Game.Entities;
using DRPGServer.Game.Npcs;
using DRPGServer.Game.World;
using DRPGServer.Network;
using DRPGServer.Network.Protocol;
using Microsoft.Extensions.Logging.Abstractions;

namespace DRPGServer.Tests;

public class NpcTests
{
    private sealed class DuplicateScriptA : NpcScriptBase
    {
        public override byte MapId => 1;
        public DuplicateScriptA() => RegisterChoice(42, DigitalBureauNpcs.NyangNpc, _ => true);
    }

    private sealed class DuplicateScriptB : NpcScriptBase
    {
        public override byte MapId => 1;
        public DuplicateScriptB() => RegisterChoice(42, DigitalBureauNpcs.NyangNpc, _ => true);
    }

    [Fact]
    public void Registry_rejects_duplicate_choice_ids_at_startup()
    {
        Assert.Throws<InvalidOperationException>(() =>
            TestSupport.NewNpcRegistry(new DuplicateScriptA(), new DuplicateScriptB()));
    }

    [Fact]
    public void Registry_enforces_map_guard_for_choices()
    {
        var world = TestSupport.NewWorld();
        var zones = new ZoneSystem(TestSupport.Data, world, TestSupport.Clock, NullLogger<ZoneSystem>.Instance);
        var registry = TestSupport.NewNpcRegistry(new VillageOfBeginningNpcs(zones), new DigitalBureauNpcs());

        var session = TestSupport.NewSession(ServerRole.Map);
        var character = Entities.Character(map: 1); // no Mapa 1, não pode executar escolha do Mapa 79
        var player = new Player(session, new AuthenticatedAccount(1, "u", Authority.None), character);

        Assert.False(registry.Choose(player, 1));
        Assert.Empty(session.DrainOutgoing());
    }

    [Fact]
    public void Nyang_choice_10_exchanges_tunomon_letter_and_replies_99_on_failure_or_repeat()
    {
        var registry = TestSupport.NewNpcRegistry(new DigitalBureauNpcs());
        var session = TestSupport.NewSession(ServerRole.Map);
        var character = Entities.Character().AtNpc(DigitalBureauNpcs.BureauMapId, DigitalBureauNpcs.NyangNpc);
        var player = new Player(session, new AuthenticatedAccount(1, "u", Authority.None), character);

        // Sem a carta do Tunomon -> falha e envia S2C 0x56 = 99 (fecha janela 0x38)
        Assert.False(registry.Choose(player, DigitalBureauNpcs.NyangIntroLetterChoice));
        var errFrame = Assert.Single(session.DrainOutgoing());
        Assert.Equal((ushort)0x56, PacketHeader.Read(errFrame).Opcode);
        Assert.Equal(NpcScriptBase.CloseWindowError, BinaryPrimitives.ReadUInt32LittleEndian(errFrame.AsSpan(PacketHeader.Length, 4)));

        // Com a carta 22008 -> consome carta e entrega Teleport X (907), Crest (107) e Rookie Net X (10026)
        character.Inventory.Main.Add(VillageOfBeginningNpcs.TunomonCard, 1);
        Assert.True(registry.Choose(player, DigitalBureauNpcs.NyangIntroLetterChoice));
        var okOpcodes = session.SentOpcodes();
        Assert.Equal([(ushort)0x56, (ushort)0xC9, (ushort)0xFC], okOpcodes);
        Assert.Equal(0u, character.Inventory.Main.Count(VillageOfBeginningNpcs.TunomonCard));
        Assert.Equal(1u, character.Inventory.Main.Count(DigitalBureauNpcs.TeleportXItemId));
        Assert.Equal(1u, character.Inventory.Main.Count(DigitalBureauNpcs.BeginnerCrestItemId));
        Assert.Equal(1u, character.Inventory.Main.Count(DigitalBureauNpcs.RookieNetItemId));
        Assert.True(character.HasFlag((ushort)DigitalBureauNpcs.NyangIntroLetterChoice));

        // Repetição -> falha e responde 99 novamente
        character.Inventory.Main.Add(VillageOfBeginningNpcs.TunomonCard, 1);
        Assert.False(registry.Choose(player, DigitalBureauNpcs.NyangIntroLetterChoice));
        var repeatFrame = Assert.Single(session.DrainOutgoing());
        Assert.Equal(NpcScriptBase.CloseWindowError, BinaryPrimitives.ReadUInt32LittleEndian(repeatFrame.AsSpan(PacketHeader.Length, 4)));
    }
}
