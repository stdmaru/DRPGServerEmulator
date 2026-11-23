using System.Buffers.Binary;
using DRPGServer.Game.Entities;
using DRPGServer.Game.World;
using DRPGServer.Network.Packets.Global;
using DRPGServer.Network.Packets.Map;
using DRPGServer.Network.Protocol;
using Microsoft.Extensions.Logging;

namespace DRPGServer.Network.Handlers;

/// <summary>0x64: primeiro pacote no global (usuário + personagem).</summary>
[PacketHandler(GlobalOpcode.UserAuth)]
public sealed class GlobalUserAuthHandler(AccountRegistry accounts, ILogger<GlobalUserAuthHandler> log) : ISessionPacketHandler
{
    public ValueTask HandleAsync(Session session, PacketReader packet, CancellationToken ct)
    {
        var username = packet.ReadFixedString(21);
        packet.ReadFixedString(21);             // personagem
        if (session.Account is null && accounts.Claim(username, session) is null)
        {
            log.LogWarning("{Session}: {Username} no global sem login no channel", session, username);
            session.Close("sem autenticação no channel");
            return ValueTask.CompletedTask;
        }
        session.Send(new UnknownE1Packet());
        session.Send(new Unknown65Packet());
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// ✅ 0x7B: enciclopédia (1104 bytes cifrados em AES-128-ECB: u32 digimonUid + u8 owned[1100]).
/// Ecoa o uid enviado pelo cliente e marca as espécies dos Digimons guardados no Digimon Depository.
/// </summary>
[PacketHandler(GlobalOpcode.EncyclopediaData)]
public sealed class EncyclopediaDataHandler(AccountRegistry accounts, GameWorld world) : ISessionPacketHandler
{
    public ValueTask HandleAsync(Session session, PacketReader packet, CancellationToken ct)
    {
        var aesKey = session.GlobalAesKey ?? GlobalSessionCipher.DeriveKey(HelloPacket.DefaultSeed);
        uint digimonUid = 0;
        if (packet.Remaining >= GlobalSessionCipher.BlockSize)
        {
            var firstBlock = GlobalSessionCipher.DecryptBlocks(aesKey, packet.ReadBytes(GlobalSessionCipher.BlockSize));
            digimonUid = BinaryPrimitives.ReadUInt32LittleEndian(firstBlock);
        }

        if (session.Account is not { } account)
        {
            session.Send(new EncyclopediaDataPacket(aesKey, digimonUid, []));
            return ValueTask.CompletedTask;
        }

        world.Post(() =>
        {
            var character = FindActiveCharacter(accounts, account.Username);
            var storedSpecies = character is null ? Array.Empty<ushort>() : CollectDepositorySpecies(character);
            session.Send(new EncyclopediaDataPacket(aesKey, digimonUid, storedSpecies));
        });
        return ValueTask.CompletedTask;
    }

    private static ushort[] CollectDepositorySpecies(Character character)
    {
        if (character.Depository.Count == 0) return [];
        var species = new HashSet<ushort>();
        foreach (var digimon in character.Depository)
        {
            var entry = EncyclopediaPartyEntry.From(digimon);
            if (entry.BaseSpecies > 0) species.Add((ushort)entry.BaseSpecies);
            if (entry.ChampionSpecies > 0) species.Add((ushort)entry.ChampionSpecies);
            if (entry.UltimateSpecies > 0) species.Add((ushort)entry.UltimateSpecies);
            if (entry.MegaSpecies > 0) species.Add((ushort)entry.MegaSpecies);
        }
        return species.ToArray();
    }

    internal static Character? FindActiveCharacter(AccountRegistry accounts, string username)
    {
        foreach (var s in accounts.SessionsOf(username))
        {
            if (s.Role == ServerRole.Map && s.Player is { } player)
                return player.Character;
        }
        return null;
    }
}

/// <summary>
/// ✅ 0x7C: time atual e evoluções desbloqueadas para a enciclopédia (64 bytes cifrados em AES-128-ECB).
/// Lê os 5 slots do personagem na thread do <see cref="GameWorld"/>.
/// </summary>
[PacketHandler(GlobalOpcode.EncyclopediaCollection)]
public sealed class EncyclopediaCollectionHandler(AccountRegistry accounts, GameWorld world) : ISessionPacketHandler
{
    public ValueTask HandleAsync(Session session, PacketReader packet, CancellationToken ct)
    {
        var aesKey = session.GlobalAesKey ?? GlobalSessionCipher.DeriveKey(HelloPacket.DefaultSeed);
        if (session.Account is not { } account)
        {
            session.Send(new EncyclopediaCollectionPacket(aesKey, []));
            return ValueTask.CompletedTask;
        }

        world.Post(() =>
        {
            var character = EncyclopediaDataHandler.FindActiveCharacter(accounts, account.Username);
            var entries = new EncyclopediaPartyEntry[Character.PartySize];
            if (character is not null)
            {
                for (int i = 0; i < Character.PartySize; i++)
                    entries[i] = EncyclopediaPartyEntry.From(character.Party[i]);
            }
            session.Send(new EncyclopediaCollectionPacket(aesKey, entries));
        });
        return ValueTask.CompletedTask;
    }
}
