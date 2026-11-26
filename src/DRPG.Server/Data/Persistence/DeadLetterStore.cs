using System.Collections.Concurrent;
using System.Text.Json;
using DRPG.Server.Configuration;
using DRPG.Server.Data.Repositories;
using DRPG.Server.Game.World;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DRPG.Server.Data.Persistence;

/// <summary>O que fica gravado em <c>character-&lt;uid&gt;.json</c>.</summary>
public sealed record DeadLetterEntry(int Version, DateTimeOffset FailedAt, string Job, string Error, CharacterSnapshot Snapshot)
{
    public const int CurrentVersion = 1;
}

/// <summary>
/// Snapshots de personagem que não chegaram ao banco depois de todas as tentativas. Fica em arquivo, não no banco: a
/// causa mais comum é o banco fora do ar. Um arquivo por personagem, com o snapshot mais novo que falhou (cada snapshot
/// é o estado inteiro). Enquanto o arquivo existe, o personagem não entra no mapa; uma gravação mais nova que dá certo,
/// ou a reaplicação do próprio arquivo, resolve. Na partida, a pasta precisa ser gravável e todo arquivo precisa ser
/// lido: senão o servidor não sobe. Seguro de qualquer thread.
/// </summary>
public sealed class DeadLetterStore
{
    private const string Prefix = "character-";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly ConcurrentDictionary<uint, CharacterSnapshot> _pending = new();
    private readonly Lock _fileLock = new();
    private readonly IGameClock _clock;
    private readonly ILogger<DeadLetterStore> _log;

    public DeadLetterStore(IOptions<PersistenceOptions> options, IGameClock clock, ILogger<DeadLetterStore> log)
    {
        _clock = clock;
        _log = log;
        Directory = Path.GetFullPath(options.Value.DeadLetterPath);
        System.IO.Directory.CreateDirectory(Directory);
        var probe = Path.Combine(Directory, ".write-test");
        try
        {
            File.WriteAllText(probe, "");
            File.Delete(probe);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"{PersistenceOptions.Section}:DeadLetterPath = '{Directory}': pasta sem permissão de escrita", e);
        }

        foreach (var file in System.IO.Directory.EnumerateFiles(Directory, Prefix + "*.json"))
        {
            var entry = Read(file);
            _pending[entry.Snapshot.Uid] = entry.Snapshot;
            log.LogCritical("Personagem {Uid} com gravação pendente no dead-letter desde {FailedAt} ({File}): bloqueado até reaplicar",
                entry.Snapshot.Uid, entry.FailedAt, file);
        }
    }

    public string Directory { get; }

    public IReadOnlyList<uint> Blocked => _pending.Keys.ToArray();

    public bool IsBlocked(uint characterUid) => _pending.ContainsKey(characterUid);

    /// <summary>O snapshot pendente agora (o mais novo que falhou), ou <c>null</c> se já foi resolvido.</summary>
    public CharacterSnapshot? Find(uint characterUid) => _pending.GetValueOrDefault(characterUid);

    /// <summary>Guarda o snapshot que não foi gravado e bloqueia o personagem. Nunca lança.</summary>
    public void Add(CharacterSnapshot snapshot, string job, Exception error)
    {
        _pending[snapshot.Uid] = snapshot;
        var entry = new DeadLetterEntry(DeadLetterEntry.CurrentVersion, _clock.UtcNow, job, $"{error.GetType().Name}: {error.Message}", snapshot);
        var json = JsonSerializer.Serialize(entry, Json);
        var file = PathOf(snapshot.Uid);
        try
        {
            lock (_fileLock)
            {
                var temp = file + ".tmp";
                File.WriteAllText(temp, json);
                File.Move(temp, file, overwrite: true);
            }
            _log.LogCritical(error, "{Job} do personagem {Uid} não chegou ao banco: snapshot em {File}, personagem bloqueado",
                job, snapshot.Uid, file);
        }
        catch (Exception e)
        {
            // último recurso: o snapshot inteiro no log, para ser reaplicado à mão
            _log.LogCritical(e, "{Job} do personagem {Uid} não chegou ao banco nem ao dead-letter ({File}). Snapshot: {Json}",
                job, snapshot.Uid, file, json);
        }
    }

    /// <summary>Uma gravação mais nova do personagem chegou ao banco: o pendente não vale mais.</summary>
    public void Resolve(uint characterUid)
    {
        if (!_pending.TryRemove(characterUid, out _)) return;
        try
        {
            lock (_fileLock)
                File.Delete(PathOf(characterUid));
            _log.LogWarning("Dead-letter do personagem {Uid} resolvido: gravação chegou ao banco", characterUid);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // o arquivo é mais velho que o banco: reaplicá-lo depois de reiniciar só regravaria um estado já superado
            _log.LogCritical(e, "Dead-letter do personagem {Uid} resolvido, mas {File} não foi apagado: apague antes de reiniciar",
                characterUid, PathOf(characterUid));
        }
    }

    private string PathOf(uint characterUid) => Path.Combine(Directory, $"{Prefix}{characterUid}.json");

    private static DeadLetterEntry Read(string file)
    {
        try
        {
            var entry = JsonSerializer.Deserialize<DeadLetterEntry>(File.ReadAllText(file), Json)
                ?? throw new InvalidDataException("arquivo vazio");
            if (entry.Version != DeadLetterEntry.CurrentVersion)
                throw new InvalidDataException($"versão {entry.Version}, esperado {DeadLetterEntry.CurrentVersion}");
            if ($"{Prefix}{entry.Snapshot.Uid}.json" != Path.GetFileName(file))
                throw new InvalidDataException($"o snapshot é do personagem {entry.Snapshot.Uid}");
            return entry;
        }
        catch (Exception e) when (e is JsonException or InvalidDataException or NotSupportedException)
        {
            throw new InvalidOperationException($"dead-letter ilegível: {file} ({e.Message}). Corrija ou mova o arquivo antes de subir", e);
        }
    }
}
