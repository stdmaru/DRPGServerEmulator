using System.Buffers.Binary;
using DRPGServer.Game;
using DRPGServer.Game.Entities;
using DRPGServer.Game.Stats;
using DRPGServer.Game.World;
using DRPGServer.Network;
using DRPGServer.Network.Handlers;
using DRPGServer.Network.Packets.Global;
using DRPGServer.Network.Packets.Map;
using DRPGServer.Network.Protocol;

namespace DRPGServer.Tests;

public class DepositoryTests
{
    private static byte[] BuildActionRequest(ushort npcId, DepositoryAction action, uint partyUid = 0, uint depUid = 0)
    {
        var buf = new byte[750];
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(0, 2), npcId);
        buf[2] = (byte)action;
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(4, 4), partyUid);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(8, 4), depUid);
        buf[32] = 0xFF;
        return buf;
    }

    [Fact]
    public void Open_fails_when_zero_slots_and_succeeds_when_slots_available()
    {
        var character = Entities.Character();
        character.DepositorySlots = 0;
        var session = TestSupport.NewSession(ServerRole.Map);
        var player = new Player(session, new AuthenticatedAccount(1, "u", Authority.None), character);
        session.Player = player;

        var handler = new DepositoryActionHandler(TestSupport.GameOptions());
        handler.Handle(session, new PacketReader(BuildActionRequest(1, DepositoryAction.Open)));

        var frames = session.DrainOutgoing();
        Assert.Equal(2, frames.Count);
        Assert.Equal((ushort)MapOpcode.DepositoryList, PacketHeader.Read(frames[0]).Opcode);
        Assert.Equal((ushort)MapOpcode.DepositoryAction, PacketHeader.Read(frames[1]).Opcode);
        Assert.Equal(0, frames[0][14 + 4]);                                  // availableSlots = 0
        Assert.Equal((byte)DepositoryResult.OpenFailed, frames[1][14 + 32]); // result = 2 (precisa expandir)

        character.DepositorySlots = 10;
        handler.Handle(session, new PacketReader(BuildActionRequest(1, DepositoryAction.Open)));

        frames = session.DrainOutgoing();
        Assert.Equal(2, frames.Count);
        Assert.Equal(10, frames[0][14 + 4]);                                 // availableSlots = 10
        Assert.Equal((byte)DepositoryResult.OpenOk, frames[1][14 + 32]);     // result = 1 (abre janela 0x29)
    }

    [Fact]
    public void Deposit_withdraw_and_swap_round_trip_with_official_bits_fee()
    {
        var character = Entities.Character();
        character.DepositorySlots = 15; // 2 páginas (10 na pág 1, 5 na pág 2)
        character.SetBits(500);

        var second = new Digimon(TestSupport.Data, TestSupport.Data.FindSpecies(14)!, 5) { Uid = 42, Name = "Seedmon" };
        Assert.True(character.Place(1, second));

        var session = TestSupport.NewSession(ServerRole.Map);
        var player = new Player(session, new AuthenticatedAccount(1, "u", Authority.None), character);
        session.Player = player;
        var handler = new DepositoryActionHandler(TestSupport.GameOptions(o => o.ChargeDepositoryFee = true));

        // Sem Bits suficientes (lv 5 custa 1000 Bits) -> DepositNotEnoughBits (5)
        handler.Handle(session, new PacketReader(BuildActionRequest(1, DepositoryAction.Deposit, partyUid: 42)));
        var poorFail = Assert.Single(session.DrainOutgoing());
        Assert.Equal((byte)DepositoryResult.DepositNotEnoughBits, poorFail[14 + 32]);
        Assert.Equal(500, character.Bits);

        // Com 5000 Bits: não pode depositar o líder (slot 0)
        character.SetBits(5_000);
        handler.Handle(session, new PacketReader(BuildActionRequest(1, DepositoryAction.Deposit, partyUid: character.Leader.Uid)));
        var leaderFail = Assert.Single(session.DrainOutgoing());
        Assert.Equal((byte)DepositoryResult.DepositFailed, leaderFail[14 + 32]);
        Assert.Equal(5_000, character.Bits);

        // Deposita o slot 1 (uid 42, lv 5 -> 1000 Bits)
        handler.Handle(session, new PacketReader(BuildActionRequest(1, DepositoryAction.Deposit, partyUid: 42)));
        var depFrames = session.DrainOutgoing();
        Assert.Equal(2, depFrames.Count);
        Assert.Equal((byte)DepositoryResult.DepositOk, depFrames[1][14 + 32]);
        Assert.Equal(4_000, character.Bits);
        Assert.Null(character.Party[1]);
        var stored = Assert.Single(character.Depository);
        Assert.Equal(42u, stored.Uid);

        // Verifica o layout de 80 bytes no 0x8A (pág 1 de 2, 10 slots na pág 1, HP > 0 em +36 para retrato colorido)
        var listPayload = depFrames[0][14..];
        Assert.Equal(808, listPayload.Length);
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(listPayload.AsSpan(0, 2)));   // currentPage = 1
        Assert.Equal(2, BinaryPrimitives.ReadUInt16LittleEndian(listPayload.AsSpan(2, 2)));   // totalPages = 2
        Assert.Equal(10, listPayload[4]);                                                      // availableSlotsOnPage = 10
        Assert.Equal(42u, BinaryPrimitives.ReadUInt32LittleEndian(listPayload.AsSpan(8, 4)));  // uid
        Assert.Equal(14u, BinaryPrimitives.ReadUInt32LittleEndian(listPayload.AsSpan(12, 4))); // speciesId
        Assert.Equal((ushort)5, BinaryPrimitives.ReadUInt16LittleEndian(listPayload.AsSpan(38, 2))); // level (+30)
        Assert.Equal(second.Hp, BinaryPrimitives.ReadInt32LittleEndian(listPayload.AsSpan(44, 4)));  // HP (+36) > 0
        Assert.True(second.Hp > 0);

        // Abre página 2 -> 5 slots disponíveis na página 2
        handler.Handle(session, new PacketReader(BuildActionRequest(2, DepositoryAction.Open)));
        var page2Frames = session.DrainOutgoing();
        Assert.Equal(2, BinaryPrimitives.ReadUInt16LittleEndian(page2Frames[0].AsSpan(14, 2)));     // currentPage = 2
        Assert.Equal(2, BinaryPrimitives.ReadUInt16LittleEndian(page2Frames[0].AsSpan(14 + 2, 2))); // totalPages = 2
        Assert.Equal(5, page2Frames[0][14 + 4]);                                                     // availableSlotsOnPage = 5

        // Troca (Swap) o líder (uid 7, lv 1 -> 1000) com o guardado (uid 42, lv 5 -> 1000) = 2000 Bits
        handler.Handle(session, new PacketReader(BuildActionRequest(1, DepositoryAction.Swap, partyUid: 7, depUid: 42)));
        var swapFrames = session.DrainOutgoing();
        Assert.Equal(2, swapFrames.Count);
        Assert.Equal((byte)DepositoryResult.SwapOk, swapFrames[1][14 + 32]);
        Assert.Equal(2_000, character.Bits);
        Assert.Equal(42u, character.Leader.Uid);
        Assert.Equal(7u, Assert.Single(character.Depository).Uid);
        Assert.Equal(42u, BinaryPrimitives.ReadUInt32LittleEndian(swapFrames[1].AsSpan(14 + 40, 4))); // DigimonStatusBlock @ 40

        // Retira o uid 7 (lv 1 -> 1000 Bits) de volta para o time
        handler.Handle(session, new PacketReader(BuildActionRequest(1, DepositoryAction.Withdraw, depUid: 7)));
        var wdFrames = session.DrainOutgoing();
        Assert.Equal(2, wdFrames.Count);
        Assert.Equal((byte)DepositoryResult.WithdrawOk, wdFrames[1][14 + 32]);
        Assert.Equal(1_000, character.Bits);
        Assert.Empty(character.Depository);
        Assert.Equal(7u, character.Party[1]!.Uid);
    }

    [Fact]
    public void Expand_consumes_item_901_or_929_and_enforces_max_ten_slots()
    {
        var character = Entities.Character();
        character.DepositorySlots = 0;
        var session = TestSupport.NewSession(ServerRole.Map);
        var player = new Player(session, new AuthenticatedAccount(1, "u", Authority.None), character);
        session.Player = player;
        var handler = new DepositoryExpandHandler();

        // Sem item no inventário -> MissingItem (1)
        handler.Handle(session, new PacketReader(new byte[15]));
        var noItem = Assert.Single(session.DrainOutgoing());
        Assert.Equal((ushort)MapOpcode.DepositoryExpand, PacketHeader.Read(noItem).Opcode);
        Assert.Equal((byte)DepositoryExpandResult.MissingItem, noItem[14]);
        Assert.Equal(0, character.DepositorySlots);

        // Com item 901 -> consome 1, sobe para 1 slot, envia 0xFC + 0x8A + 0x94(0)
        character.Inventory.Main.Place(character.Inventory.Create(DepositoryExpandHandler.ExpansionItemId, 0, 1, 100));
        handler.Handle(session, new PacketReader(new byte[15]));
        var okFrames = session.DrainOutgoing();
        Assert.Equal(3, okFrames.Count);
        Assert.Equal((ushort)MapOpcode.InventoryRefresh, PacketHeader.Read(okFrames[0]).Opcode);
        Assert.Equal((ushort)MapOpcode.DepositoryList, PacketHeader.Read(okFrames[1]).Opcode);
        Assert.Equal((ushort)MapOpcode.DepositoryExpand, PacketHeader.Read(okFrames[2]).Opcode);
        Assert.Equal((byte)DepositoryExpandResult.Ok, okFrames[2][14]);
        Assert.Equal(1, character.DepositorySlots);
        Assert.Empty(character.Inventory.Main.Items);

        // No máximo (10) -> MaxSlots (2), não consome item 929
        character.DepositorySlots = Character.MaxDepositorySlots;
        character.Inventory.Main.Place(character.Inventory.Create(DepositoryExpandHandler.EventExpansionItemId, 0, 1, 101));
        handler.Handle(session, new PacketReader(new byte[15]));
        var maxFrame = Assert.Single(session.DrainOutgoing());
        Assert.Equal((byte)DepositoryExpandResult.MaxSlots, maxFrame[14]);
        Assert.Single(character.Inventory.Main.Items);
    }

    [Fact]
    public async Task Global_0x7B_lights_up_species_stored_in_depository()
    {
        var accounts = new AccountRegistry(TestSupport.GameOptions(), TimeProvider.System);
        var world = TestSupport.NewWorld();
        var account = new AuthenticatedAccount(1, "tester", Authority.None);

        var channelSession = TestSupport.NewSession(ServerRole.Channel);
        accounts.SignIn(account, channelSession);

        var character = Entities.Character();
        character.DepositorySlots = 10;
        var stored = new Digimon(TestSupport.Data, TestSupport.Data.FindSpecies(50)!, 21) { Uid = 99, Name = "Rookiemon" };
        stored.UnlockStage(); // libera Champion (80)
        Assert.True(character.AddToDepository(stored));

        var mapSession = TestSupport.NewSession(ServerRole.Map);
        accounts.Claim("tester", mapSession);
        mapSession.Player = new Player(mapSession, account, character);

        var globalSession = TestSupport.NewSession(ServerRole.Global);
        new GlobalLifecycle(accounts).OnConnected(globalSession);
        accounts.Claim("tester", globalSession);
        var key = globalSession.GlobalAesKey!;
        globalSession.DrainOutgoing();

        var reqPlain = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(reqPlain, character.Leader.Uid);
        var reqCipher = GlobalSessionCipher.EncryptBlocks(key, reqPlain);

        var handler = new EncyclopediaDataHandler(accounts, world);
        await world.StartAsync(CancellationToken.None);
        try
        {
            await handler.HandleAsync(globalSession, new PacketReader(reqCipher), CancellationToken.None);
            await world.InvokeAsync(() => true);
        }
        finally
        {
            await world.StopAsync(CancellationToken.None);
        }

        var reply = Assert.Single(globalSession.DrainOutgoing())[14..];
        var pt = GlobalSessionCipher.DecryptBlocks(key, reply);
        Assert.Equal(character.Leader.Uid, BinaryPrimitives.ReadUInt32LittleEndian(pt.AsSpan(0, 4)));
        Assert.Equal(1, pt[4 + 50]); // espécie base no Depository
        Assert.Equal(1, pt[4 + 80]); // Champion desbloqueado no Depository
        Assert.Equal(0, pt[4 + 10]); // líder está no time (vai no 0x7C), não no Depository
    }
}
