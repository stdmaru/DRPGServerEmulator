using Dapper;
using DRPGServer.Data.GameData;
using DRPGServer.Data.Persistence;
using DRPGServer.Data.Repositories;
using DRPGServer.Game;
using DRPGServer.Game.Economy;
using DRPGServer.Game.Entities;
using DRPGServer.Game.Items;
using DRPGServer.Game.World;
using DRPGServer.Network;
using DRPGServer.Network.Handlers;
using DRPGServer.Network.Packets.Map;
using DRPGServer.Network.Protocol;
using Microsoft.Extensions.Logging.Abstractions;

namespace DRPGServer.Tests;

/// <summary>Trilha de auditoria da economia (S9): registro no personagem e gravação junto com o snapshot.</summary>
public class EconomyTests
{
    private static readonly EconomyReason Gm = new(EconomySource.Gm, "grant", "teste");

    /// <summary>"Banco" que guarda os snapshots e pode falhar.</summary>
    private sealed class RecordingStore : ICharacterSnapshotStore
    {
        public volatile bool Down;
        public List<CharacterSnapshot> Saved { get; } = [];

        public Task SaveAsync(CharacterSnapshot snapshot, CancellationToken ct)
        {
            if (Down) throw new TimeoutException("banco fora");
            lock (Saved) Saved.Add(snapshot);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void Currency_changes_record_the_real_delta_and_skip_zero()
    {
        var c = Entities.Character();
        c.SetCoins(Wallet.MaxCoins - 10);

        c.Wallet.Credit(CurrencyKind.Coins, 50, Gm);                       // teto: só 10 entraram
        c.Wallet.TrySpend(CurrencyKind.Medals, 0, Gm);                // zero: nada
        Assert.False(c.Wallet.TrySpend(CurrencyKind.Bits, 1, Gm));      // sem saldo: nada
        c.Wallet.CreditBits(0.4, Gm);                       // arredonda para 0: nada

        var entry = Assert.Single(c.Economy.Pending);
        Assert.Equal((EconomyAsset.Coins, 10L, (long?)Wallet.MaxCoins), (entry.Asset, entry.Amount, entry.BalanceAfter));
        Assert.Equal((1u, (uint?)1u, EconomySource.Gm, "grant", "teste"), (entry.AccountUid, entry.CharacterUid, entry.Source, entry.Action, entry.Detail));
    }

    [Fact]
    public void Wallet_applies_each_cap_and_reports_what_was_credited()
    {
        var w = Entities.Character().Wallet;
        w.Load(double.NaN, ushort.MaxValue - 1, 30_000, uint.MaxValue - 2);
        Assert.Equal((0d, (ushort)Wallet.MaxCoins), (w.Bits, w.Coins));     // carga também respeita o teto

        Assert.Equal(1, w.Credit(CurrencyKind.Medals, 5, Gm));
        Assert.Equal(0, w.Credit(CurrencyKind.Coins, 5, Gm));
        Assert.Equal(2, w.Credit(CurrencyKind.LadderPoints, 5, Gm));
        Assert.Equal(1_000_000_000_000, w.Credit(CurrencyKind.Bits, 1_000_000_000_000, Gm));
        Assert.Equal(ushort.MaxValue, w.Balance(CurrencyKind.Medals));

        Assert.Throws<ArgumentOutOfRangeException>(() => w.TrySpend(CurrencyKind.Bits, -1, Gm));
        Assert.Throws<ArgumentOutOfRangeException>(() => w.Credit(CurrencyKind.Bits, -1, Gm));
        Assert.Throws<ArgumentOutOfRangeException>(() => w.CreditBits(double.PositiveInfinity, Gm));
    }

    [Fact]
    public void Bits_keep_the_fraction_but_spending_uses_whole_bits()
    {
        var w = Entities.Character().Wallet;
        w.CreditBits(9.5, Gm);
        Assert.False(w.TrySpend(CurrencyKind.Bits, 10, Gm));
        Assert.True(w.TrySpend(CurrencyKind.Bits, 9, Gm));
        Assert.Equal(0.5, w.Bits);
        w.CreditBits(0.5, Gm);
        Assert.Equal(1, w.Balance(CurrencyKind.Bits));
    }

    [Fact]
    public void Digimon_depository_fee_is_audited()
    {
        var c = Entities.Character();
        var partner = Entities.Digimon(level: 10);
        typeof(Digimon).GetProperty(nameof(Digimon.Uid))!.SetValue(partner, 8u);
        Assert.True(c.Place(1, partner));
        c.SetBits(5000);
        c.DepositorySlots = 10;

        Assert.Equal(DepositoryResult.DepositOk, c.Deposit(8, 1, configuredFee: 1000));

        var fee = Assert.Single(c.Economy.Pending);
        Assert.Equal((EconomySource.DigimonDepository, "deposit-fee", -1000L, (long?)4000), (fee.Source, fee.Action, fee.Amount, fee.BalanceAfter));
    }

    [Fact]
    public void Npc_grant_and_exchange_are_audited()
    {
        var session = TestSupport.NewSession();
        var player = new Player(session, new AuthenticatedAccount(1, "u", Authority.None), Entities.Character());
        var npc = new EconomyReason(EconomySource.Npc, "choice", "escolha 7");

        Assert.True(TestSupport.Items().GiveItems(player, [new(25, 5)], npc, refresh: false));
        Assert.True(TestSupport.Items().Exchange(player, 25, 5, [new(1)], npc, refresh: false));

        Assert.Equal([(EconomyAsset.Item, 25u, 5L), (EconomyAsset.Item, 25u, -5L), (EconomyAsset.Item, 1u, 1L)],
            player.Character.Economy.Pending.Select(e => (e.Asset, e.AssetId, e.Amount)));
    }

    [Fact]
    public void Drop_pickup_is_audited()
    {
        var world = TestSupport.NewWorld();
        var zones = new ZoneSystem(TestSupport.Data, world, TestSupport.Clock, NullLogger<ZoneSystem>.Instance);
        var session = TestSupport.NewSession(ServerRole.Map);
        var character = Entities.Character();
        var player = new Player(session, new AuthenticatedAccount(1, "u", Authority.None), character);
        session.Player = player;
        zones.Find(79)!.Enter(player);
        var drop = zones.Find(79)!.Drop(41, 56, 56, character.Uid);

        var w = new PacketWriter();
        new ItemMoveHeader(ItemPlace.Ground, ItemPlace.Inventory, 56, 56, 0, 0).Write(w);
        w.WriteU32(drop.Uid);
        w.WriteU32(drop.ItemId);
        new ItemMoveHandler(TestSupport.Guard()).Dispatch(session, new PacketReader(w.Payload.ToArray()));

        var entry = Assert.Single(character.Economy.Pending);
        Assert.Equal((EconomySource.DropPickup, EconomyAsset.Item, 41u, 1L), (entry.Source, entry.Asset, entry.AssetId, entry.Amount));
    }

    [Fact]
    public async Task Saved_snapshot_carries_the_audit_and_the_commit_clears_it()
    {
        await using var harness = new WorldHarness();
        await harness.StartAsync();
        var store = new RecordingStore();
        var players = NewPlayers(harness, store);
        var character = Entities.Character();
        var player = await harness.InvokeAsync(() => players.EnterNow(TestSupport.NewSession(ServerRole.Map),
            new AuthenticatedAccount(1, "u", Authority.None), character));

        await harness.RunAsync(() =>
        {
            character.Wallet.Credit(CurrencyKind.Bits, 100, Gm);
            player.RequestImmediateSave();
            players.Update(harness.World.Now, TimeSpan.Zero);
        });
        await harness.SettleAsync();

        var saved = Assert.Single(store.Saved);
        Assert.Equal(100, Assert.Single(saved.Economy).Amount);
        Assert.Empty(await harness.InvokeAsync(() => character.Economy.Pending.ToList()));
    }

    [Fact]
    public async Task Failed_save_keeps_the_audit_for_the_next_snapshot_and_the_dead_letter()
    {
        await using var harness = new WorldHarness();
        await harness.StartAsync();
        var store = new RecordingStore { Down = true };
        var players = NewPlayers(harness, store);
        var character = Entities.Character();
        var player = await harness.InvokeAsync(() => players.EnterNow(TestSupport.NewSession(ServerRole.Map),
            new AuthenticatedAccount(1, "u", Authority.None), character));

        await harness.RunAsync(() =>
        {
            character.Wallet.Credit(CurrencyKind.Bits, 100, Gm);
            player.RequestImmediateSave();
            players.Update(harness.World.Now, TimeSpan.Zero);
        });
        await harness.SettleAsync();
        Assert.Single(harness.DeadLetters.Find(character.Uid)!.Economy);
        Assert.Single(await harness.InvokeAsync(() => character.Economy.Pending.ToList()));

        store.Down = false;
        await harness.RunAsync(() =>
        {
            character.Wallet.Credit(CurrencyKind.Bits, 5, Gm);
            player.RequestImmediateSave();
            players.Update(harness.World.Now, TimeSpan.Zero);
        });
        await harness.SettleAsync();

        Assert.Equal([100L, 5L], Assert.Single(store.Saved).Economy.Select(e => e.Amount));
        Assert.False(harness.DeadLetters.IsBlocked(character.Uid));
        Assert.Empty(await harness.InvokeAsync(() => character.Economy.Pending.ToList()));
    }

    [Fact]
    public void Dead_letter_keeps_the_audit_across_a_restart()
    {
        var c = Entities.Character();
        c.Wallet.Credit(CurrencyKind.Bits, 42, Gm);
        var first = TestSupport.DeadLetters();
        first.Add(CharacterSnapshot.Of(c), "character.save", new TimeoutException());

        var entry = Assert.Single(TestSupport.DeadLetters(first.Directory).Find(c.Uid)!.Economy);
        Assert.Equal(c.Economy.Pending[0], entry);
    }

    [Fact]
    public void Long_detail_is_cut_to_the_column_size()
    {
        var entry = EconomyEntry.Create(DateTimeOffset.UnixEpoch, 1, null, new(EconomySource.Gm, "grant", new string('x', 400)),
            EconomyAsset.Cash, 0, 1, 1);
        Assert.Equal(EconomyEntry.DetailLength, entry.Detail!.Length);
    }

    private static PlayerSystem NewPlayers(WorldHarness harness, ICharacterSnapshotStore store)
    {
        var zones = new ZoneSystem(TestSupport.Data, harness.World, TestSupport.Clock, NullLogger<ZoneSystem>.Instance);
        return new PlayerSystem(harness.World, zones, TestSupport.NewBattles(harness.World), harness.Persistence, store,
            TestSupport.GameOptions(), NullLogger<PlayerSystem>.Instance);
    }
}

/// <summary>SQL do <c>economy_log</c>: na mesma transação, sem duplicar.</summary>
public class EconomyLogDatabaseTests(DatabaseFixture db) : IClassFixture<DatabaseFixture>
{
    private readonly DigimonUids _uids = new();
    private static readonly EconomyReason Gm = new(EconomySource.Gm, "grant", "teste");

    // ordem fixa para comparar: UUIDv7 no mesmo milissegundo não segue a ordem de inserção
    private async Task<List<(string Source, string Asset, uint AssetId, long Amount, uint? Character)>> RowsAsync(uint account)
    {
        await using var conn = await db.Database.OpenAsync();
        var rows = await conn.QueryAsync<(string, string, uint, long, uint?)>(
            "SELECT source, asset, asset_id, amount, character_uid FROM economy_log WHERE account_uid = @account ORDER BY source, asset, asset_id",
            new { account });
        return rows.ToList();
    }

    [DatabaseFact]
    public async Task Snapshot_audit_is_written_once_even_if_saved_again()
    {
        _uids.Seed(50_000);
        var (account, characterUid) = await db.NewCharacterAsync(_uids, "Audit1");
        var characters = new CharacterRepository(db.Database, _uids, new EconomyLogRepository());
        var c = Entities.Character();
        c.Wallet.Credit(CurrencyKind.Bits, 300, Gm);
        c.Economy.RecordItems(Gm, [(25, 2)], cards: false);
        var entries = c.Economy.Pending.Select(e => e with { AccountUid = account, CharacterUid = characterUid }).ToList();
        var snapshot = CharacterSnapshot.Of(c) with { Uid = characterUid, Digimons = [], Forms = [], Titles = [], Economy = entries };

        await characters.SaveAsync(snapshot, default);
        await characters.SaveAsync(snapshot, default);   // confirmação perdida, dead-letter: mesmo id

        Assert.Equal([("Gm", "Bits", 0u, 300L, (uint?)characterUid), ("Gm", "Item", 25u, 2L, characterUid)], await RowsAsync(account));
    }

    [DatabaseFact]
    public async Task Cash_purchase_and_gm_grant_audit_in_their_transaction()
    {
        _uids.Seed(60_000);
        var (account, _) = await db.NewCharacterAsync(_uids, "Audit2");
        var cash = new CashRepository(db.Database, TestSupport.Clock, new EconomyLogRepository());
        await cash.AddCashAsync(account, 100, 20, Gm, default);

        var buy = new EconomyReason(EconomySource.CashShop, "buy", "produto 1");
        var outcome = await cash.PurchaseAsync(account, 1, 1, 50, 50, [new AccountStorageEntry(CashShopItemKind.Item, 718, 1)], buy, default);
        Assert.Equal(CashPurchaseResult.Ok, outcome.Result);

        Assert.Equal([
            ("CashShop", "BonusCash", 0u, -20L, (uint?)null), ("CashShop", "Cash", 0u, -30L, null), ("CashShop", "Item", 718u, 1L, null),
            ("Gm", "BonusCash", 0u, 20L, null), ("Gm", "Cash", 0u, 100L, null),
        ], await RowsAsync(account));
    }

    [DatabaseFact]
    public async Task Refused_purchase_writes_no_audit()
    {
        _uids.Seed(70_000);
        var (account, _) = await db.NewCharacterAsync(_uids, "Audit3");
        var cash = new CashRepository(db.Database, TestSupport.Clock, new EconomyLogRepository());

        var outcome = await cash.PurchaseAsync(account, 1, 1, 50, 50, [new AccountStorageEntry(CashShopItemKind.Item, 718, 1)],
            new EconomyReason(EconomySource.CashShop, "buy"), default);

        Assert.Equal(CashPurchaseResult.InsufficientCash, outcome.Result);
        Assert.Empty(await RowsAsync(account));
    }

    [DatabaseFact]
    public async Task Refused_transfer_writes_no_audit()
    {
        _uids.Seed(80_000);
        var (account, characterUid) = await db.NewCharacterAsync(_uids, "Audit4");
        var cash = new CashRepository(db.Database, TestSupport.Clock, new EconomyLogRepository());
        var characters = new CharacterRepository(db.Database, _uids, new EconomyLogRepository());
        var transfers = new AccountTransferRepository(db.Database, cash, characters);
        var full = Enumerable.Range(0, CashRepository.DepositoryCapacityPerKind)
            .Select(_ => new AccountStorageEntry(CashShopItemKind.Item, 1, 1)).ToList();
        var empty = CharacterSnapshot.Of(Entities.Character()) with { Uid = characterUid, Digimons = [], Forms = [], Titles = [] };
        Assert.True(await transfers.DepositAsync(account, full, empty, default));

        var audit = EconomyEntry.Create(DateTimeOffset.UtcNow, account, null, Gm, EconomyAsset.Item, 2, 1, null);
        Assert.False(await transfers.DepositAsync(account, [new AccountStorageEntry(CashShopItemKind.Item, 2, 1)],
            empty with { Economy = [audit] }, default));

        Assert.Empty(await RowsAsync(account));
    }
}
