using DRPGServer.Data.Persistence;
using DRPGServer.Data.Repositories;
using DRPGServer.Game.Entities;
using DRPGServer.Network;
using Microsoft.Extensions.Logging;

namespace DRPGServer.Game.World;

/// <summary>Carrega um personagem do banco, pronto para o mundo. Null se não existe ou é inválido.</summary>
public interface ICharacterLoader
{
    Task<Character?> LoadAsync(uint accountUid, string name, CancellationToken ct);
}

public sealed class CharacterLoader(CharacterRepository characters, CharacterFactory factory) : ICharacterLoader
{
    public async Task<Character?> LoadAsync(uint accountUid, string name, CancellationToken ct) =>
        await characters.FindAsync(accountUid, name, ct) is { } record ? factory.Build(record) : null;
}

/// <summary>
/// Entrada no mapa (docs/emulador/seguranca.md#entrada-e-saída-do-mundo): marca a conta como <c>Loading</c> no
/// <see cref="PlayerSystem"/>, espera o último save da conta chegar ao banco, carrega o personagem fora da thread do
/// mundo e só então o coloca no mundo.
/// </summary>
public sealed class WorldEntry(
    GameWorld world, PlayerSystem players, ICharacterLoader characters, DeadLetterStore deadLetters, ILogger<WorldEntry> log)
{
    /// <summary>
    /// Roda fora da thread do mundo. <paramref name="entered"/> roda na thread do mundo logo depois de o jogador entrar.
    /// Falso: a entrada foi recusada ou o personagem não carregou, e a sessão deve fechar.
    /// </summary>
    public async Task<bool> EnterAsync(Session session, AuthenticatedAccount account, string name, Action<Player> entered,
        CancellationToken ct)
    {
        var start = await world.InvokeAsync(() => players.BeginEntry(session, account));
        if (!start.Accepted)
        {
            log.LogWarning("{Session}: entrada recusada, outra entrada da conta está em andamento", session);
            return false;
        }

        Character? character = null;
        try
        {
            await start.Ready.WaitAsync(ct);
            character = await characters.LoadAsync(account.Uid, name, ct);
            if (character is not null && deadLetters.IsBlocked(character.Uid))
            {
                log.LogWarning("{Session}: {Name} bloqueado, a última gravação dele está no dead-letter", session, name);
                character = null;   // o finally encerra o Loading sem jogador
                return false;
            }
        }
        finally
        {
            // sempre fecha o Loading, também com falha ou cancelamento
            var loaded = character;
            world.Post(() =>
            {
                if (players.CompleteEntry(session, account, loaded) is { } player)
                    entered(player);
            });
        }

        if (character is null)
            log.LogWarning("{Session}: personagem {Name} não encontrado ou inválido", session, name);
        return character is not null;
    }
}
