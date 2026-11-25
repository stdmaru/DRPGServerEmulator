using System.Buffers.Binary;
using System.Security.Cryptography;
using DRPGServer.Game.Entities;
using DRPGServer.Game.World;
using DRPGServer.Network.Packets.Global;
using DRPGServer.Network.Packets.Map;
using DRPGServer.Network.Protocol;
using Microsoft.Extensions.Logging;

namespace DRPGServer.Network.Handlers;

/// <summary>0x64: primeiro pacote no global (usuário + personagem).</summary>
[PacketHandler(GlobalOpcode.UserAuth)]
public sealed class GlobalUserAuthHandler(AccountRegistry accounts, ILogger<GlobalUserAuthHandler> log)
    : SessionHandler<GlobalUserAuthRequest>
{
    protected override ValueTask HandleAsync(Session session, GlobalUserAuthRequest request, CancellationToken ct)
    {
        // repetido na mesma conexão: só responde de novo (a conta não troca)
        var claim = session.Account is null ? accounts.Claim(request.Username, session) : default;
        if (session.Account is null)
        {
            log.LogWarning("{Session}: {Username} recusado no global ({Reason})", session, request.Username, claim.Rejection);
            session.Close("ticket do channel inválido");
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
public sealed class EncyclopediaDataHandler(AccountRegistry accounts, GameWorld world) : AccountHandler<EncyclopediaDataRequest>
{
    protected override ValueTask HandleAsync(Session session, AuthenticatedAccount account, EncyclopediaDataRequest request, CancellationToken ct)
    {
        var aesKey = session.GlobalAesKey ?? GlobalSessionCipher.DeriveKey(HelloPacket.DefaultSeed);
        var firstBlock = GlobalSessionCipher.DecryptBlocks(aesKey, request.FirstBlock);
        uint digimonUid = BinaryPrimitives.ReadUInt32LittleEndian(firstBlock);

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
public sealed class EncyclopediaCollectionHandler(AccountRegistry accounts, GameWorld world) : AccountHandler<EncyclopediaCollectionRequest>
{
    protected override ValueTask HandleAsync(Session session, AuthenticatedAccount account, EncyclopediaCollectionRequest request, CancellationToken ct)
    {
        var aesKey = session.GlobalAesKey ?? GlobalSessionCipher.DeriveKey(HelloPacket.DefaultSeed);

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

/// <summary>Boas-vindas (0x0E) no global; ao desconectar, solta a conta.</summary>
public sealed class GlobalLifecycle(AccountRegistry accounts) : ISessionLifecycle
{
    public ServerRole Role => ServerRole.Global;

    public void OnConnected(Session session)
    {
        var seed = RandomNumberGenerator.GetBytes(GlobalSessionCipher.BlockSize);
        session.GlobalAesKey = GlobalSessionCipher.DeriveKey(seed);
        session.Send(new HelloPacket((ushort)GlobalOpcode.Hello, seed));
    }

    public void OnDisconnected(Session session) => accounts.Release(session);
}
