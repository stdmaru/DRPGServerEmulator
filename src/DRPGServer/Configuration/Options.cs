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

    public int DigimonMaxLevel { get; set; } = 100;
    public int MaxStackPerItem { get; set; } = 999;
    public double ExpRate { get; set; } = 1;
    public double BitRate { get; set; } = 1;

    /// <summary>Quarto digimon na criação: 0 Chibimon, 1 Yarmon, 2 Hopmon, 3 Dorimon.</summary>
    public int FourthStarterDigimon { get; set; }

    /// <summary>Mapa e posição de quem acabou de criar o personagem.</summary>
    public byte StartMapId { get; set; } = 79;
    public short StartPosX { get; set; } = 56;
    public short StartPosY { get; set; } = 56;

    /// <summary>Intervalo do salvamento automático de quem está online.</summary>
    public int AutosaveSeconds { get; set; } = 300;

    /// <summary>Por quanto tempo o login no channel vale para entrar no mapa depois que a conexão do channel cai.</summary>
    public int AccountTicketGraceSeconds { get; set; } = 120;
}
