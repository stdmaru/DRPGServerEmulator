using DRPGServer.Data.Repositories;

namespace DRPGServer.Data.Persistence;

/// <summary>
/// Um trabalho da <see cref="PersistenceQueue"/>. <see cref="Run"/> devolve <c>true</c> quando gravou o snapshot do
/// personagem <see cref="CharacterUid"/>: o dead-letter dele, se houver, fica resolvido (o snapshot é o estado inteiro e
/// é mais novo). Exceção conta como falha e é repetida (<see cref="PersistenceService"/>).
/// </summary>
/// <param name="Name">Nome curto para o log (ex.: <c>character.save</c>).</param>
public sealed record PersistenceJob(string Name, Func<CancellationToken, Task<bool>> Run)
{
    /// <summary>Personagem cujo snapshot este trabalho grava.</summary>
    public uint? CharacterUid { get; init; }

    /// <summary>Snapshot que vai para o dead-letter se as tentativas acabarem. Sem ele, a desistência é só log crítico.</summary>
    public CharacterSnapshot? DeadLetter { get; init; }

    public static PersistenceJob SaveCharacter(ICharacterSnapshotStore store, CharacterSnapshot snapshot) =>
        new("character.save", async ct =>
        {
            await store.SaveAsync(snapshot, ct);
            return true;
        })
        {
            CharacterUid = snapshot.Uid,
            DeadLetter = snapshot,
        };
}
