using DRPG.Shared.Formats.Pack;
using DRPG.Server.Configuration;
using DRPG.Server.Data;
using DRPG.Server.Data.GameData;
using DRPG.Server.Data.Persistence;
using DRPG.Server.Data.Repositories;
using DRPG.Server.Game.Accounts;
using DRPG.Server.Game.Battle;
using DRPG.Server.Game.Commands;
using DRPG.Server.Game.Digimons;
using DRPG.Server.Game.Items;
using DRPG.Server.Game.Npcs;
using DRPG.Server.Game.Shop;
using DRPG.Server.Game.World;
using DRPG.Server.Network;
using DRPG.Server.Network.Handlers;
using DRPG.Server.Network.Traffic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;


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
// opções validadas na partida (Configuration/OptionsValidation.cs): valor fora do intervalo não sobe o servidor
services.AddOptions<ServersOptions>().Bind(builder.Configuration.GetSection(ServersOptions.Section)).ValidateOnStart();
services.AddOptions<DatabaseOptions>().Bind(builder.Configuration.GetSection(DatabaseOptions.Section)).ValidateOnStart();
services.AddOptions<PersistenceOptions>().Bind(builder.Configuration.GetSection(PersistenceOptions.Section)).ValidateOnStart();
services.AddOptions<NetworkOptions>().Bind(builder.Configuration.GetSection(NetworkOptions.Section)).ValidateOnStart();
services.AddOptions<GameOptions>().Bind(builder.Configuration.GetSection(GameOptions.Section)).ValidateOnStart();
services.AddOptions<LauncherLoginOptions>().Bind(builder.Configuration.GetSection(LauncherLoginOptions.Section)).ValidateOnStart();
services.AddSingleton<IValidateOptions<ServersOptions>, ServersOptionsValidator>();
services.AddSingleton<IValidateOptions<DatabaseOptions>, DatabaseOptionsValidator>();
services.AddSingleton<IValidateOptions<PersistenceOptions>, PersistenceOptionsValidator>();
services.AddSingleton<IValidateOptions<NetworkOptions>, NetworkOptionsValidator>();
services.AddSingleton<IValidateOptions<GameOptions>, GameOptionsValidator>();
services.AddSingleton<IValidateOptions<LauncherLoginOptions>, LauncherLoginOptionsValidator>();
services.AddSingleton(TimeProvider.System);
services.AddSingleton<IGameClock, GameClock>();

// dados
services.AddSingleton<Database>();
services.AddSingleton<DatabaseMigrator>();
services.AddSingleton<AccountRepository>();
services.AddSingleton<IAccountStore>(sp => sp.GetRequiredService<AccountRepository>());
services.AddSingleton<AccountAuthenticator>();
services.AddSingleton<ILauncherAccountStore>(sp => sp.GetRequiredService<AccountRepository>());
services.AddSingleton<ISecondaryPasswordStore>(sp => sp.GetRequiredService<AccountRepository>());
services.AddSingleton<SecondaryPasswordService>();
services.AddSingleton<LauncherServerKey>();
services.AddSingleton<LoginAttemptLimiter>();
services.AddSingleton<LauncherLoginService>();
services.AddSingleton<DRPG.Server.Game.DigimonUids>();
services.AddSingleton<IAuditLog, EconomyLogRepository>();
services.AddSingleton<CharacterRepository>();
services.AddSingleton<ICharacterSnapshotStore>(sp => sp.GetRequiredService<CharacterRepository>());
services.AddSingleton<CashRepository>();
services.AddSingleton<ICashStore>(sp => sp.GetRequiredService<CashRepository>());
services.AddSingleton<IAccountTransferStore, AccountTransferRepository>();
services.AddSingleton<IExpEventStore, ExpEventRepository>();
services.AddSingleton(sp =>
{
    var o = sp.GetRequiredService<IOptions<GameOptions>>().Value;
    var resources = Path.IsPathRooted(o.ResourcesPath) ? o.ResourcesPath : Path.Combine(AppContext.BaseDirectory, o.ResourcesPath);
    var table = Path.IsPathRooted(o.ClientTablePath) ? o.ClientTablePath : Path.Combine(AppContext.BaseDirectory, o.ClientTablePath);
    var grids = Path.Combine(Path.GetDirectoryName(table)!, ClientExtractor.GridsFile);
    return GameDataLoader.Load(resources, PackArchive.Open(table), File.Exists(grids) ? MapGrid.Load(grids) : null);
});

// ordem importa: serviços param na ordem inversa (rede → I/O do mundo → mundo → persistência)
services.AddSingleton<PersistenceQueue>();
services.AddSingleton<DeadLetterStore>();
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
services.AddHostedService<DeadLetterReplay>();
services.AddSingleton<CharacterFactory>();
services.AddSingleton<ICharacterLoader, CharacterLoader>();
services.AddSingleton<WorldEntry>();
services.AddSingleton<CharacterTransfers>();
services.AddSingleton<InteractionGuard>();
services.AddSingleton<ItemService>();
services.AddSingleton<NpcShopService>();
services.AddSingleton<NpcScriptBase, VillageOfBeginningNpcs>();
services.AddSingleton<NpcScriptBase, DigitalBureauNpcs>();
services.AddSingleton<NpcScriptRegistry>();
services.AddSingleton<Taming>();
services.AddSingleton<ItemUseService>();
services.AddSingleton<CashShopService>();
services.AddSingleton<ChatCommands>();

// rede
services.AddSingleton<AccountRegistry>();
services.AddSingleton<PacketTrace>();
services.AddSingleton<ConnectionLimiter>();
services.AddSingleton<TrafficLimits>();
services.AddSingleton<CharacterListService>();
services.AddPacketHandlers();

using var host = builder.Build();
var log = host.Services.GetRequiredService<ILogger<Program>>();

// todas as seções de uma vez, antes de qualquer serviço ler a configuração
var invalidOptions = new List<string>();
foreach (var read in new Action[]
{
    () => _ = host.Services.GetRequiredService<IOptions<ServersOptions>>().Value,
    () => _ = host.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value,
    () => _ = host.Services.GetRequiredService<IOptions<PersistenceOptions>>().Value,
    () => _ = host.Services.GetRequiredService<IOptions<NetworkOptions>>().Value,
    () => _ = host.Services.GetRequiredService<IOptions<GameOptions>>().Value,
    () => _ = host.Services.GetRequiredService<IOptions<LauncherLoginOptions>>().Value,
})
{
    try { read(); }
    catch (OptionsValidationException e) { invalidOptions.AddRange(e.Failures); }
}
if (invalidOptions.Count > 0)
{
    log.LogCritical("Configuração inválida (appsettings*.json / DRPG_*):{Errors}",
        string.Concat(invalidOptions.Select(f => Environment.NewLine + "  - " + f)));
    return 1;
}

try
{
    var data = host.Services.GetRequiredService<GameData>();
    log.LogInformation("Tabelas: {Species} digimons, {Items} itens, {Drops} grupos de drop, {Zones} zonas, {Portals} portais, {Grids} grades",
        data.Species.Count, data.Items.Count, data.DropGroups.Count, data.ZoneIds.Count, data.Portals.Values.Sum(p => p.Count), data.Grids.Count);
    if (data.Grids.Count == 0)
        log.LogWarning("Sem grids.bin: movimento e selvagens sem validação de célula. Rode extract-client (docs/cliente/arquivos-de-dados.md)");

    // a pasta do dead-letter precisa ser gravável, e o que já está nela bloqueia os personagens desde a partida
    var deadLetters = host.Services.GetRequiredService<DeadLetterStore>();
    log.LogInformation("Dead-letter em {Path} ({Count} pendentes)", deadLetters.Directory, deadLetters.Blocked.Count);

    using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5)))
    {
        await host.Services.GetRequiredService<DatabaseMigrator>().MigrateAsync(cts.Token);
        var converted = await host.Services.GetRequiredService<AccountAuthenticator>().ConvertPlainKeysAsync(cts.Token);
        if (converted > 0) log.LogInformation("{Count} auth_key em texto puro convertida(s) para hash", converted);
        var passwords = await host.Services.GetRequiredService<LauncherLoginService>().ConvertPlainPasswordsAsync(cts.Token);
        if (passwords > 0) log.LogInformation("{Count} plain-text password(s) converted to hash", passwords);
        var maxUid = await host.Services.GetRequiredService<CharacterRepository>().MaxDigimonUidAsync(cts.Token);
        host.Services.GetRequiredService<DRPG.Server.Game.DigimonUids>().Seed(maxUid);
        await host.Services.GetRequiredService<ExpEventService>().ReloadAsync(forceNotify: false, cts.Token);
    }

    host.Services.GetRequiredService<LauncherServerKey>();   // creates the key on first start and logs its fingerprint

    // os sistemas se registram no mundo ao serem criados; criar antes da thread do mundo subir
    var world = host.Services.GetRequiredService<GameWorld>();
    host.Services.GetRequiredService<ZoneSystem>();
    host.Services.GetRequiredService<BattleSystem>();
    var players = host.Services.GetRequiredService<PlayerSystem>();
    host.Services.GetRequiredService<ExpEventService>().Changed += active =>
        world.Post(() =>
        {
            foreach (var player in players.Online)
                player.Send(new DRPG.Server.Network.Packets.Map.ExpEventPacket(player.Session.MapAesKey, active));
        });
}
catch (Exception e)
{
    log.LogCritical(e, "Falha na partida");
    return 1;
}

await host.RunAsync();
return 0;
