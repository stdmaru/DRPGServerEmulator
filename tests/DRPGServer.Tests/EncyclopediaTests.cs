using System.Buffers.Binary;
using DRPGServer.Data.Repositories;
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

public class EncyclopediaTests
{
    [Fact]
    public void Cipher_derives_official_session_key_from_hello_seed()
    {
        // ✅ Sessão 58 conn 184 (captures.db): hello 0x0E → chave AES K = AES_C(hello XOR W)
        var hello = Convert.FromHexString("1dd89edb291be41b6d863d7e3d6fbd95");
        var key = GlobalSessionCipher.DeriveKey(hello);
        Assert.Equal("9EFA6F0B44A3025AAA98E6A3AC04C4D7", Convert.ToHexString(key));
    }

    [Fact]
    public void Cipher_decrypts_captured_0x7C_sample_to_expected_uid_and_species()
    {
        // ✅ Amostra v242 capturada: uid 200000 no slot 0 e espécie base 10
        var key = GlobalSessionCipher.DeriveKey(HelloPacket.DefaultSeed);
        var ciphertext = Convert.FromHexString(
            "A5EFC317402AD990EF76B213CF81A812" +
            "8F114E2FCB44E06689741F78A04EF1C9" +
            "33090087C7BFBAC2FA667C0731D9FE1B" +
            "33090087C7BFBAC2FA667C0731D9FE1B");

        var plaintext = GlobalSessionCipher.DecryptBlocks(key, ciphertext);
        Assert.Equal(200_000u, BinaryPrimitives.ReadUInt32LittleEndian(plaintext.AsSpan(0, 4)));
        Assert.Equal((short)10, BinaryPrimitives.ReadInt16LittleEndian(plaintext.AsSpan(20, 2)));
        Assert.Equal((short)0, BinaryPrimitives.ReadInt16LittleEndian(plaintext.AsSpan(30, 2)));
    }

    [Fact]
    public void Packet_0x7B_encrypts_uid_and_owned_species_flags()
    {
        var key = GlobalSessionCipher.DeriveKey(HelloPacket.DefaultSeed);
        var packet = new EncyclopediaDataPacket(key, 232_066u, [10, 50, 80, 1099, 1200]);

        var encrypted = packet.EncodePayload();
        Assert.Equal(1104, encrypted.Length);

        var plaintext = GlobalSessionCipher.DecryptBlocks(key, encrypted);
        Assert.Equal(232_066u, BinaryPrimitives.ReadUInt32LittleEndian(plaintext.AsSpan(0, 4)));
        Assert.Equal(1, plaintext[4 + 10]);
        Assert.Equal(1, plaintext[4 + 50]);
        Assert.Equal(1, plaintext[4 + 80]);
        Assert.Equal(1, plaintext[4 + 1099]);
        Assert.Equal(0, plaintext[4 + 14]);
    }

    [Fact]
    public void Packet_0x7C_encodes_five_party_slots_and_evolution_stages()
    {
        var key = GlobalSessionCipher.DeriveKey(HelloPacket.DefaultSeed);
        var entries = new[]
        {
            new EncyclopediaPartyEntry(101, 50, 80, 0, 0),
            new EncyclopediaPartyEntry(202, 10, 0, 0, 0),
        };

        var encrypted = new EncyclopediaCollectionPacket(key, entries).EncodePayload();
        Assert.Equal(64, encrypted.Length);

        var pt = GlobalSessionCipher.DecryptBlocks(key, encrypted);
        Assert.Equal(101u, BinaryPrimitives.ReadUInt32LittleEndian(pt.AsSpan(0, 4)));
        Assert.Equal(202u, BinaryPrimitives.ReadUInt32LittleEndian(pt.AsSpan(4, 4)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(pt.AsSpan(8, 4)));

        Assert.Equal((short)50, BinaryPrimitives.ReadInt16LittleEndian(pt.AsSpan(20, 2)));
        Assert.Equal((short)10, BinaryPrimitives.ReadInt16LittleEndian(pt.AsSpan(22, 2)));
        Assert.Equal((short)80, BinaryPrimitives.ReadInt16LittleEndian(pt.AsSpan(30, 2)));
        Assert.Equal((short)0, BinaryPrimitives.ReadInt16LittleEndian(pt.AsSpan(32, 2)));
        Assert.Equal(new byte[4], pt[60..64]);
    }

    [Fact]
    public void Party_entry_reflects_tamed_and_evolved_digimons()
    {
        var character = Entities.Character();
        var uids = new DigimonUids();
        uids.Seed(50);
        var taming = new Taming(TestSupport.Data, uids, TestSupport.GameOptions());
        var tamed = taming.Tame(character, TestSupport.Data.FindSpecies(14)!)!;

        // Evolução na árvore de teste: In-Training (14) -> Rookie (50) -> Champion (80)
        var player = new Player(TestSupport.NewSession(), new AuthenticatedAccount(1, "u", Authority.None), character);
        while (tamed.Level < StatFormulas.StageLevel(StatFormulas.Rookie)) tamed.LevelUp();
        Progression.AfterLevelUp(player, tamed, inBattle: false);

        while (tamed.Level < StatFormulas.StageLevel(StatFormulas.Champion)) tamed.LevelUp();
        Progression.AfterLevelUp(player, tamed, inBattle: false);

        // 0x7C reflete a forma-base (50) e o Champion desbloqueado (80) do capturado + espécie 10 do líder
        var leaderEntry = EncyclopediaPartyEntry.From(character.Leader);
        Assert.Equal((short)10, leaderEntry.BaseSpecies);
        Assert.Equal((short)0, leaderEntry.ChampionSpecies);

        var tamedEntry = EncyclopediaPartyEntry.From(tamed);
        Assert.Equal((short)50, tamedEntry.BaseSpecies);
        Assert.Equal((short)80, tamedEntry.ChampionSpecies);
    }

    [Fact]
    public async Task Global_handlers_reply_with_live_character_party_and_echoed_uid()
    {
        var accounts = new AccountRegistry(TestSupport.GameOptions(), TimeProvider.System);
        var world = TestSupport.NewWorld();
        var account = new AuthenticatedAccount(1, "tester", Authority.None);

        var channelSession = TestSupport.NewSession(ServerRole.Channel);
        accounts.SignIn(account, channelSession);

        var character = new Character(Entities.Digimon(species: 50, level: 11), new Inventory(TestSupport.Data.FindItem, 999, _ => null))
        {
            Uid = 1, AccountUid = 1, Slot = 0, Name = "Tester", TamerId = 1, MapId = 79, X = 56, Y = 56,
        };
        var mapSession = TestSupport.NewSession(ServerRole.Map);
        accounts.Claim("tester", mapSession);
        mapSession.Player = new Player(mapSession, account, character);

        var globalSession = TestSupport.NewSession(ServerRole.Global);
        var lifecycle = new GlobalLifecycle(accounts);
        lifecycle.OnConnected(globalSession);
        accounts.Claim("tester", globalSession);

        // Extrai o hello enviado no OnConnected e verifica a chave derivada
        var helloFrame = Assert.Single(globalSession.DrainOutgoing());
        var helloSeed = helloFrame.AsSpan(14, 16);
        var key = GlobalSessionCipher.DeriveKey(helloSeed);
        Assert.Equal(key, globalSession.GlobalAesKey);

        // C2S 0x7B: bloco 0 cifrado com uid do líder
        var req7BPlain = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(req7BPlain, character.Leader.Uid);
        var req7BCipher = GlobalSessionCipher.EncryptBlocks(key, req7BPlain);

        var dataHandler = new EncyclopediaDataHandler(accounts, world);
        var collectionHandler = new EncyclopediaCollectionHandler(accounts, world);

        await world.StartAsync(CancellationToken.None);
        try
        {
            await dataHandler.DispatchAsync(globalSession, new PacketReader(req7BCipher), CancellationToken.None);
            await collectionHandler.DispatchAsync(globalSession, new PacketReader(new byte[64]), CancellationToken.None);
            await world.InvokeAsync(() => true);
        }
        finally
        {
            await world.StopAsync(CancellationToken.None);
        }

        var replies = globalSession.DrainOutgoing();
        Assert.Equal(2, replies.Count);

        // Valida S2C 0x7B (uid ecoado e array zerado até existir Digimon Depository)
        var reply7B = replies[0][14..];
        Assert.Equal(1104, reply7B.Length);
        var pt7B = GlobalSessionCipher.DecryptBlocks(key, reply7B);
        Assert.Equal(character.Leader.Uid, BinaryPrimitives.ReadUInt32LittleEndian(pt7B.AsSpan(0, 4)));
        Assert.True(pt7B.AsSpan(4).IndexOfAnyExcept((byte)0) < 0);

        // Valida S2C 0x7C (time atual)
        var reply7C = replies[1][14..];
        Assert.Equal(64, reply7C.Length);
        var pt7C = GlobalSessionCipher.DecryptBlocks(key, reply7C);
        Assert.Equal(character.Leader.Uid, BinaryPrimitives.ReadUInt32LittleEndian(pt7C.AsSpan(0, 4)));
        Assert.Equal((short)50, BinaryPrimitives.ReadInt16LittleEndian(pt7C.AsSpan(20, 2)));
    }
}
