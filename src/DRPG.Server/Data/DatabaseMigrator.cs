using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using DRPG.Server.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace DRPG.Server.Data;

public sealed record Migration(int Version, string Name, string Path, string Checksum);

/// <summary>
/// Aplica os sql/upgrade/NNNN_*.sql que ainda não estão em <c>schema_version</c>.
/// Roda na partida, antes de abrir os servidores. Ver sql/upgrade/README.md.
/// </summary>
public sealed partial class DatabaseMigrator(Database database, IOptions<DatabaseOptions> options, TimeProvider time, ILogger<DatabaseMigrator> log)
{
    [GeneratedRegex(@"^(\d{4})_(.+)\.sql$")]
    private static partial Regex FileName();

    public static IReadOnlyList<Migration> Discover(string directory)
    {
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"pasta de migrações {directory} não encontrada");
        var list = new List<Migration>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.sql"))
        {
            var match = FileName().Match(System.IO.Path.GetFileName(path));
            if (!match.Success) continue;
            var checksum = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
            list.Add(new Migration(int.Parse(match.Groups[1].Value), match.Groups[2].Value, path, checksum));
        }
        var duplicated = list.GroupBy(m => m.Version).FirstOrDefault(g => g.Count() > 1);
        if (duplicated is not null)
            throw new InvalidOperationException($"duas migrações com o número {duplicated.Key:D4}");
        return list.OrderBy(m => m.Version).ToList();
    }

    public async Task MigrateAsync(CancellationToken ct)
    {
        var o = options.Value;
        var directory = System.IO.Path.IsPathRooted(o.MigrationsPath)
            ? o.MigrationsPath
            : System.IO.Path.Combine(AppContext.BaseDirectory, o.MigrationsPath);
        var migrations = Discover(directory);

        await WaitForServerAsync(TimeSpan.FromSeconds(o.ConnectTimeoutSeconds), ct);
        await using (var server = new MySqlConnection(database.ServerConnectionString))
        {
            await server.OpenAsync(ct);
            await server.ExecuteAsync($"CREATE DATABASE IF NOT EXISTS `{database.DatabaseName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_uca1400_ai_ci");
        }

        await using var db = await database.OpenAsync(ct);
        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS `schema_version` (
              `version` int NOT NULL PRIMARY KEY,
              `name` varchar(200) NOT NULL,
              `checksum` char(64) NOT NULL,
              `applied_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
            """);
        var applied = (await db.QueryAsync<(int Version, string Checksum)>("SELECT version, checksum FROM schema_version"))
            .ToDictionary(r => r.Version, r => r.Checksum);

        foreach (var m in migrations.Where(m => applied.ContainsKey(m.Version)))
            if (applied[m.Version] != m.Checksum)
                throw new InvalidOperationException(
                    $"a migração {m.Version:D4}_{m.Name} já aplicada foi alterada (checksum diferente). Crie uma migração nova em vez de editar.");

        var pending = migrations.Where(m => !applied.ContainsKey(m.Version)).ToList();
        if (pending.Count == 0)
        {
            log.LogInformation("Banco {Db} na versão {Version}, nada a aplicar", database.DatabaseName, applied.Keys.DefaultIfEmpty().Max());
            return;
        }

        foreach (var m in pending)
        {
            log.LogInformation("Aplicando migração {Version:D4}_{Name}", m.Version, m.Name);
            var sql = await File.ReadAllTextAsync(m.Path, Encoding.UTF8, ct);
            await db.ExecuteAsync(new CommandDefinition(sql, cancellationToken: ct, commandTimeout: 300));
            await db.ExecuteAsync("INSERT INTO schema_version (version, name, checksum) VALUES (@Version, @Name, @Checksum)", m);
        }
        log.LogInformation("Banco {Db} atualizado para a versão {Version}", database.DatabaseName, pending[^1].Version);
    }

    private async Task WaitForServerAsync(TimeSpan timeout, CancellationToken ct)
    {
        long started = time.GetTimestamp();
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await using var connection = new MySqlConnection(database.ServerConnectionString);
                await connection.OpenAsync(ct);
                return;
            }
            catch (MySqlException e) when (time.GetElapsedTime(started) < timeout)
            {
                if (attempt == 1 || attempt % 10 == 0)
                    log.LogWarning("Banco ainda não responde ({Message}); tentando de novo", e.Message);
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
        }
    }
}
