using DRPGServer.Network;

namespace DRPGServer.Configuration;

public sealed class EndpointOptions
{
    public string BindIP { get; set; } = "0.0.0.0";
    public int Port { get; set; }
}

/// <summary>Seção "Servers" do appsettings.json.</summary>
public sealed class ServersOptions
{
    public const string Section = "Servers";

    public EndpointOptions Login { get; set; } = new() { Port = 7001 };
    public EndpointOptions Channel { get; set; } = new() { Port = 7000 };
    public EndpointOptions Map { get; set; } = new() { Port = 7002 };
    public EndpointOptions Global { get; set; } = new() { Port = 7003 };
}

public sealed class DatabaseOptions
{
    public const string Section = "Database";

    public string Host { get; set; } = "database";
    public int Port { get; set; } = 3306;
    public string User { get; set; } = "root";
    public string Password { get; set; } = "root";
    public string Name { get; set; } = "drpg";

    /// <summary>Pasta com os NNNN_*.sql. Relativa ao executável se não for absoluta.</summary>
    public string MigrationsPath { get; set; } = "sql/upgrade";

    /// <summary>Tempo máximo esperando o banco subir na partida.</summary>
    public int ConnectTimeoutSeconds { get; set; } = 60;
}

/// <summary>Seção "Persistence": gravações em segundo plano e dead-letter (docs/emulador/seguranca.md#persistência).</summary>
public sealed class PersistenceOptions
{
    public const string Section = "Persistence";

    /// <summary>Tentativas de cada gravação. Em queda (a anterior desistiu) e na parada, uma só.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Espera antes da 2ª tentativa; dobra a cada uma, até <see cref="RetryMaxMilliseconds"/>.</summary>
    public int RetryBaseMilliseconds { get; set; } = 1000;
    public int RetryMaxMilliseconds { get; set; } = 30_000;

    /// <summary>
    /// Pasta dos snapshots que não chegaram ao banco. Relativa à pasta de trabalho do processo (não à do executável:
    /// ela precisa sobreviver a um novo build ou deploy).
    /// </summary>
    public string DeadLetterPath { get; set; } = "dead-letter";

    /// <summary>Intervalo para reaplicar o dead-letter dos personagens offline.</summary>
    public int ReplaySeconds { get; set; } = 60;
}

/// <summary>Seção "Network": autenticação entre servidores e limites de recurso (docs/emulador/seguranca.md).</summary>
public sealed class NetworkOptions
{
    public const string Section = "Network";

    /// <summary>
    /// Validade do ticket emitido pelo channel depois que a última sessão da conta fecha. Enquanto alguma sessão da
    /// conta está aberta (o channel fica aberto durante o jogo), o ticket não vence.
    /// </summary>
    public int AccountTicketSeconds { get; set; } = 60;

    /// <summary>Conexões abertas ao mesmo tempo, somando os quatro servidores. Passou: a conexão nova é recusada.</summary>
    public int MaxConnections { get; set; } = 4000;

    /// <summary>
    /// Conexões abertas ao mesmo tempo vindas de um IP, somando os quatro servidores. ✅ Um cliente em jogo mantém 3
    /// (channel, mapa e global; o login fecha logo); 16 deixa cinco clientes atrás do mesmo NAT.
    /// </summary>
    public int MaxConnectionsPerIp { get; set; } = 16;

    /// <summary>
    /// Prazo para a sessão se autenticar depois de conectar (channel 0x04, mapa 0x01, global 0x64). No login, que não
    /// autentica a sessão, é o tempo máximo de conexão. ✅ Nas capturas o primeiro pacote chega em até 0,3 s.
    /// </summary>
    public int AuthTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Tempo sem receber pacote no login, channel e global. ✅ O cliente manda o keepalive 0xC4 a cada 165–168 s
    /// nesses servidores (42 intervalos medidos).
    /// </summary>
    public int IdleTimeoutSeconds { get; set; } = 600;

    /// <summary>
    /// Tempo sem receber pacote no mapa. ✅ O keepalive 0xC4 vem a cada 3,7–6,6 s (5 083 intervalos), e o maior
    /// silêncio medido no mapa foi 5,7 s, incluindo a carga do mapa.
    /// </summary>
    public int MapIdleTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Teto geral de pacotes por sessão (balde de fichas). Passou: a sessão fecha. ✅ Pico medido de 7 pacotes em 1 s,
    /// 40 em 10 s e 195 em 60 s.
    /// </summary>
    public RateLimitOptions Packets { get; set; } = new() { PerSecond = 20, Burst = 60 };

    /// <summary>
    /// Tetos por opcode, por servidor e nome do opcode (<c>Map</c> → <c>ChatNormal</c>). Passou: o pacote é ignorado,
    /// a sessão continua (o teto geral ainda vale). ✅ Movimento: pico de 2 em 1 s e 12 em 10 s.
    /// </summary>
    public Dictionary<ServerRole, Dictionary<string, RateLimitOptions>> OpcodeLimits { get; set; } = new()
    {
        [ServerRole.Map] = new()
        {
            [nameof(MapOpcode.ChatNormal)] = new() { PerSecond = 1, Burst = 5 },
            [nameof(MapOpcode.MoveCharacter)] = new() { PerSecond = 10, Burst = 20 },
        },
    };

    /// <summary>
    /// Pacotes de uma sessão esperando a thread do mundo. Passou: a sessão fecha, para um cliente não atrasar o tick
    /// de todos.
    /// </summary>
    public int MaxPendingWorldWork { get; set; } = 64;
}

/// <summary>Balde de fichas: até <see cref="Burst"/> de uma vez, repostas a <see cref="PerSecond"/> por segundo.</summary>
public sealed class RateLimitOptions
{
    public double PerSecond { get; set; }
    public int Burst { get; set; }
}

/// <summary>Seção "Game": regras ajustáveis (antigo ServerConsts.xml).</summary>
public sealed class GameOptions
{
    public const string Section = "Game";

    /// <summary>Pasta das tabelas XML. Relativa ao executável se não for absoluta.</summary>
    public string ResourcesPath { get; set; } = "Resources";

    /// <summary>
    /// Caminho do <c>table.hf</c> do cliente (o <c>table.pf</c> fica ao lado). Relativo ao executável se não for
    /// absoluto. Padrão: Resources/Client, fora do git (copie do cliente v322; docs/projeto/legal.md).
    /// </summary>
    public string ClientTablePath { get; set; } = "Resources/Client/table.hf";

    /// <summary>❓ 160 por ora (o oficial tem personagens acima de 200; o limite real não é conhecido).</summary>
    public int DigimonMaxLevel { get; set; } = 160;
    public int MaxStackPerItem { get; set; } = 999;
    public double ExpRate { get; set; } = 1;
    public double BitRate { get; set; } = 1;

    /// <summary>
    /// Chance da captura comum na batalha (0..1). ❓ No oficial é muito baixa e não há amostra de sucesso (21 falhas na
    /// sessão 56); 10 % aqui para testar.
    /// </summary>
    public double CaptureChance { get; set; } = 0.10;

    /// <summary>Quarto digimon na criação: 0 Chibimon, 1 Yarmon, 2 Hopmon, 3 Dorimon.</summary>
    public int FourthStarterDigimon { get; set; }

    /// <summary>Mapa e posição de quem acabou de criar o personagem.</summary>
    public byte StartMapId { get; set; } = 79;
    public short StartPosX { get; set; } = 56;
    public short StartPosY { get; set; } = 56;

    /// <summary>Intervalo (segundos) para gravar jogadores marcados como dirty (eventos críticos gravam imediatamente).</summary>
    public int AutosaveSeconds { get; set; } = 60;

    /// <summary>Intervalo (segundos) para consultar a tabela <c>exp_event</c> no banco de dados. Padrão: 60 s.</summary>
    public int EventPollSeconds { get; set; } = 60;

    /// <summary>
    /// Distância máxima (casas, maior eixo) entre o jogador e o NPC para usar loja, Digimon Center e escolhas.
    /// ✅ Nas capturas, todo uso veio de até 3 casas (21 lojas/Depository, 30 escolhas); 5 dá folga.
    /// </summary>
    public int NpcInteractionRange { get; set; } = 5;

    /// <summary>
    /// Distância máxima (casas, maior eixo) até o selvagem para começar batalha ou usar Taming Net.
    /// ✅ 156 de 158 inícios de batalha capturados vieram de até 5 casas, 1 de 7 (o selvagem andando); 8 dá folga.
    /// ❓ Taming Net no campo sem captura: usa o mesmo valor.
    /// </summary>
    public int WildInteractionRange { get; set; } = 8;

    /// <summary>
    /// Distância máxima (casas, maior eixo) até o item no chão para coletar.
    /// ✅ 26 de 27 coletas capturadas vieram de até 1 casa (o cliente anda até o item), 1 de 3; 4 dá folga.
    /// </summary>
    public int DropPickupRange { get; set; } = 4;

    /// <summary>Slots iniciais liberados no Digimon Depository ao criar personagem (0..210). Padrão 10; 0 exige item (901/929).</summary>
    public byte DefaultDepositorySlots { get; set; } = 10;

    /// <summary>
    /// Taxa em Bits por Digimon no Depository: <c>0</c> = gratuito, <c>&gt; 0</c> = valor fixo em Bits,
    /// <c>&lt; 0</c> = tabela oficial por nível (0x00BDFF10: 1k..70k). Padrão: <c>1000</c>.
    /// </summary>
    public long DigimonDepositoryFee { get; set; } = 1000;

    /// <summary>
    /// Se <c>true</c>, permite subir vários níveis de uma vez preservando o excedente de EXP.
    /// Se <c>false</c> (padrão oficial), sobe apenas 1 nível por ganho de EXP e descarta o restante (EXP = 0).
    /// </summary>
    public bool AllowMultiLevelUp { get; set; } = false;
}

/// <summary>Launcher login on the LoginServer (docs/protocolo/launcher.md).</summary>
public sealed class LauncherLoginOptions
{
    public const string Section = "LauncherLogin";

    /// <summary>Off: the hello is answered with <c>Disabled</c> and no login is accepted.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// PKCS#8 PEM file with the LoginServer ECDH P-256 key. Created on first start when missing; relative paths are
    /// under the server folder. Keep it out of git; its fingerprint is logged at start for <c>DR_Configuration.json</c>.
    /// </summary>
    public string KeyPath { get; set; } = "launcher-login.pem";

    /// <summary>Hours a game key issued by the launcher stays valid.</summary>
    public double GameKeyValidHours { get; set; } = 5;

    /// <summary>Lowest launcher version allowed to log in (0 = any).</summary>
    public uint MinLauncherVersion { get; set; }

    /// <summary>Login attempts per address: bucket refilled at <c>PerSecond</c>, up to <c>Burst</c>.</summary>
    public RateLimitOptions AttemptsPerAddress { get; set; } = new() { PerSecond = 0.1, Burst = 10 };

    /// <summary>Login attempts per login name, from any address.</summary>
    public RateLimitOptions AttemptsPerLogin { get; set; } = new() { PerSecond = 0.05, Burst = 5 };
}
