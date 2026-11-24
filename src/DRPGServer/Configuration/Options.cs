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

/// <summary>Seção "Database".</summary>
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

    /// <summary>Por quanto tempo o login no channel vale para entrar no mapa depois que a conexão do channel cai.</summary>
    public int AccountTicketGraceSeconds { get; set; } = 120;

    /// <summary>Slots iniciais liberados no Digimon Depository ao criar personagem (0..210). Padrão 10; 0 exige item (901/929).</summary>
    public byte DefaultDepositorySlots { get; set; } = 10;

    /// <summary>
    /// Taxa em Bits por Digimon no Depository: <c>0</c> = gratuito, <c>&gt; 0</c> = valor fixo em Bits,
    /// <c>&lt; 0</c> = tabela oficial por nível (0x00BDFF10: 1k..70k). Padrão: <c>1000</c>.
    /// </summary>
    public double DigimonDepositoryFee { get; set; } = 1000;

    /// <summary>
    /// Se <c>true</c>, permite subir vários níveis de uma vez preservando o excedente de EXP.
    /// Se <c>false</c> (padrão oficial), sobe apenas 1 nível por ganho de EXP e descarta o restante (EXP = 0).
    /// </summary>
    public bool AllowMultiLevelUp { get; set; } = false;
}
