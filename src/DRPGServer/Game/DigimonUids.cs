namespace DRPGServer.Game;

/// <summary>
/// Uids de digimon. O digimon capturado precisa de uid na hora (vai no 0xEF), dentro da thread do mundo, que não
/// espera o banco: os uids são reservados em memória a partir do maior do banco na partida e o insert usa o uid
/// explícito. A criação de personagem (channel) usa o mesmo contador. Seguro entre threads.
/// </summary>
public sealed class DigimonUids
{
    private long _last;

    /// <summary>Maior uid já usado (lido do banco na partida).</summary>
    public void Seed(uint maxUsed) => Interlocked.Exchange(ref _last, Math.Max(Interlocked.Read(ref _last), maxUsed));

    public uint Next() => (uint)Interlocked.Increment(ref _last);
}
