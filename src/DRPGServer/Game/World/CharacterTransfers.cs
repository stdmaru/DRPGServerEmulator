using DRPGServer.Data.Persistence;
using DRPGServer.Data.Repositories;
using DRPGServer.Game.Entities;
using Microsoft.Extensions.Logging;

namespace DRPGServer.Game.World;

/// <summary>Como uma transferência terminou.</summary>
public enum TransferOutcome
{
    /// <summary>Conta e personagem gravados juntos.</summary>
    Committed,
    /// <summary>O banco recusou (depósito cheio, linha já retirada). Nada foi gravado.</summary>
    Refused,
    /// <summary>Erro de banco. Nada foi gravado.</summary>
    Failed,
}

/// <summary>
/// Transferências conta ↔ personagem (docs/emulador/seguranca.md#transferências-entre-conta-e-personagem):
/// <list type="number">
/// <item>Na thread do mundo, quem chama já validou e aplicou a mudança no personagem. <see cref="Run"/> põe o jogador
/// em <see cref="PlayerStates.Transacting"/> e tira o snapshot.</item>
/// <item>A <see cref="PersistenceQueue"/> roda <c>commit</c>, que grava a mudança da conta e o snapshot numa
/// transação só.</item>
/// <item><c>settled</c> roda na thread do mundo com o resultado, também com o jogador offline: fora de
/// <see cref="TransferOutcome.Committed"/>, é ele que desfaz a mudança no personagem. Depois, o jogador volta ao
/// estado normal, e um rollback é gravado de novo.</item>
/// </list>
/// Enquanto a transferência não termina, o <see cref="PlayerSystem"/> não grava o jogador: um snapshot com a mudança
/// fora da transação poderia chegar ao banco sem a parte da conta.
/// </summary>
public sealed class CharacterTransfers(GameWorld world, PersistenceQueue persistence, ILogger<CharacterTransfers> log)
{
    /// <param name="operation">Nome curto para o log (ex.: <c>shop-depository.deposit</c>).</param>
    /// <param name="commit">Fora da thread do mundo: grava conta + snapshot. Falso = recusado.</param>
    /// <param name="settled">Na thread do mundo, com o jogador online ou não.</param>
    public void Run(Player player, string operation, Func<CharacterSnapshot, CancellationToken, Task<bool>> commit,
        Action<TransferOutcome> settled)
    {
        world.AssertWorldThread();
        if (player.State != PlayerStates.Idle)
            throw new InvalidOperationException($"{player}: transferência {operation} fora de Idle ({player.State})");

        player.BeginTransfer();
        var snapshot = CharacterSnapshot.Of(player.Character);
        // uma tentativa só, sem dead-letter: a falha é desfeita no personagem, e o rollback é gravado como save comum
        persistence.EnqueueCharacterTransfer(new PersistenceJob(operation, async ct =>
        {
            TransferOutcome outcome;
            try
            {
                outcome = await commit(snapshot, ct) ? TransferOutcome.Committed : TransferOutcome.Refused;
            }
            catch (Exception e)
            {
                log.LogError(e, "Transferência {Operation} falhou ({Player}); desfazendo no personagem", operation, player);
                outcome = TransferOutcome.Failed;
            }
            world.Post(() => Settle(player, operation, outcome, settled));
            return outcome == TransferOutcome.Committed;   // o snapshot foi gravado junto com a conta
        })
        {
            CharacterUid = snapshot.Uid,
        });
    }

    private void Settle(Player player, string operation, TransferOutcome outcome, Action<TransferOutcome> settled)
    {
        try
        {
            settled(outcome);
        }
        catch (Exception e)
        {
            log.LogError(e, "Erro ao concluir a transferência {Operation} ({Outcome}, {Player})", operation, outcome, player);
        }
        finally
        {
            // o rollback precisa chegar ao banco: saves da fila foram descartados pelo snapshot da transferência
            if (outcome != TransferOutcome.Committed)
                player.RequestImmediateSave();
            player.EndTransfer();
        }
    }
}
