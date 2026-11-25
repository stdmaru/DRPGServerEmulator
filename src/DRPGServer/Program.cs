using Dapper;
using DRPGServer.Configuration;
using DRPGServer.Data;
using DRPGServer.Data.GameData;
using DRPGServer.Data.Persistence;
using DRPGServer.Data.Repositories;
using DRPGServer.Game.Battle;
using DRPGServer.Game.Commands;
using DRPGServer.Game.Npcs;
using DRPGServer.Game.Shop;
using DRPGServer.Game.World;
using DRPGServer.Network;
using DRPGServer.Network.Handlers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

DefaultTypeMap.MatchNamesWithUnderscores = true;

// extract-client <pasta do cliente> <saída>: copia o pacote de tabelas e gera o grids.bin (docs/cliente/arquivos-de-dados.md)
if (args is ["extract-client", var clientDir, var outDir])
{
    var r = ClientExtractor.Extract(clientDir, outDir);
    Console.WriteLine($"{r.Grids} de {r.Maps} mapas com grade em {Path.GetFullPath(outDir)}");
    foreach (var m in r.Missing) Console.WriteLine($"  sem .map: {m}");
    return 0;
}

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});
builder.Configuration
    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables("DRPG_");
// toda dependência é obrigatória: o container confere o grafo inteiro na partida, também em produção
builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
{
    ValidateOnBuild = true,
    ValidateScopes = true,
}));
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss.fff ";
});

var services = builder.Services;
services.Configure<ServersOptions>(builder.Configuration.GetSection(ServersOptions.Section));
services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.Section));
services.Configure<GameOptions>(builder.Configuration.GetSection(GameOptions.Section));
services.AddSingleton(TimeProvider.System);
services.AddSingleton<IGameClock, GameClock>();

// dados
services.AddSingleton<Database>();
services.AddSingleton<DatabaseMigrator>();
services.AddSingleton<AccountRepository>();
services.AddSingleton<DRPGServer.Game.DigimonUids>();
services.AddSingleton<CharacterRepository>();
services.AddSingleton<ICharacterSnapshotStore>(sp => sp.GetRequiredService<CharacterRepository>());
services.AddSingleton<ICashStore, CashRepository>();
services.AddSingleton<IExpEventStore, ExpEventRepository>();
services.AddSingleton(sp =>
{
    var o = sp.GetRequiredService<IOptions<GameOptions>>().Value;
    var resources = Path.IsPathRooted(o.ResourcesPath) ? o.ResourcesPath : Path.Combine(AppContext.BaseDirectory, o.ResourcesPath);
    var table = Path.IsPathRooted(o.ClientTablePath) ? o.ClientTablePath : Path.Combine(AppContext.BaseDirectory, o.ClientTablePath);
    var grids = Path.Combine(Path.GetDirectoryName(table)!, ClientExtractor.GridsFile);
    return GameDataLoader.Load(resources, ClientPack.Open(table), File.Exists(grids) ? MapGrid.Load(grids) : null);
});

// ordem importa: serviços param na ordem inversa (rede → I/O do mundo → mundo → persistência)
services.AddSingleton<PersistenceQueue>();
services.AddHostedService<PersistenceService>();
services.AddSingleton<GameWorld>();
services.AddHostedService(sp => sp.GetRequiredService<GameWorld>());
services.AddSingleton<WorldIo>();
services.AddHostedService(sp => sp.GetRequiredService<WorldIo>());   // para antes do mundo: espera o I/O pendente
services.AddSingleton<NetworkHost>();
services.AddHostedService(sp => sp.GetRequiredService<NetworkHost>());

// jogo
services.AddSingleton<ExpEventService>();
services.AddHostedService(sp => sp.GetRequiredService<ExpEventService>());
services.AddSingleton<ZoneSystem>();
services.AddSingleton<BattleSystem>();
services.AddSingleton<PlayerSystem>();
services.AddSingleton<CharacterFactory>();
services.AddSingleton<NpcScriptBase, VillageOfBeginningNpcs>();
services.AddSingleton<NpcScriptBase, DigitalBureauNpcs>();
services.AddSingleton<NpcScriptRegistry>();
services.AddSingleton<Taming>();
services.AddSingleton<CashShopService>();
services.AddSingleton<ChatCommands>();

// rede
services.AddSingleton<AccountRegistry>();
services.AddSingleton<PacketTrace>();
services.AddSingleton<CharacterListService>();
services.AddPacketHandlers();

using var host = builder.Build();
var log = host.Services.GetRequiredService<ILogger<Program>>();

try
{
    var data = host.Services.GetRequiredService<GameData>();
    log.LogInformation("Tabelas: {Species} digimons, {Items} itens, {Drops} grupos de drop, {Zones} zonas, {Portals} portais, {Grids} grades",
        data.Species.Count, data.Items.Count, data.DropGroups.Count, data.ZoneIds.Count, data.Portals.Values.Sum(p => p.Count), data.Grids.Count);
    if (data.Grids.Count == 0)
        log.LogWarning("Sem grids.bin: movimento e selvagens sem validação de célula. Rode extract-client (docs/cliente/arquivos-de-dados.md)");

    using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5)))
    {
        await host.Services.GetRequiredService<DatabaseMigrator>().MigrateAsync(cts.Token);
        var maxUid = await host.Services.GetRequiredService<CharacterRepository>().MaxDigimonUidAsync(cts.Token);
        host.Services.GetRequiredService<DRPGServer.Game.DigimonUids>().Seed(maxUid);
        await host.Services.GetRequiredService<ExpEventService>().ReloadAsync(forceNotify: false, cts.Token);
    }

    // os sistemas se registram no mundo ao serem criados; criar antes da thread do mundo subir
    var world = host.Services.GetRequiredService<GameWorld>();
    host.Services.GetRequiredService<ZoneSystem>();
    host.Services.GetRequiredService<BattleSystem>();
    var players = host.Services.GetRequiredService<PlayerSystem>();
    host.Services.GetRequiredService<ExpEventService>().Changed += active =>
        world.Post(() =>
        {
            foreach (var player in players.Online)
                player.Send(new DRPGServer.Network.Packets.Map.ExpEventPacket(player.Session.MapAesKey, active));
        });
}
catch (Exception e)
{
    log.LogCritical(e, "Falha na partida");
    return 1;
}

await host.RunAsync();
return 0;
