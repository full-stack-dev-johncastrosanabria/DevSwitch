namespace DevSwitch;

/// <summary>The switches shown in the window. Add a line here to control another service.</summary>
public static class Catalog
{
    public const string ComposeFile = @"C:\dev\databases\compose.yaml";
    public const string ComposeProject = "devdb";

    public static IReadOnlyList<Switchable> Create() =>
    [
        new ServiceGroup("SQL Server 2025",
            "Instancia nativa MSSQLSERVER en localhost:1433, con su agente.",
            core: ["MSSQLSERVER", "SQLSERVERAGENT"],
            extras: ["SQLTELEMETRY", "MSSQLFDLauncher", "MSSQLLaunchpad", "SQLBrowser"]),
        new ServiceGroup("MySQL 26.10",
            "Servicio nativo en localhost:3306. Contraseña de root en C:\\dev\\databases\\.env.",
            core: ["MySQL"],
            extras: []),
        new DockerDesktopSwitch(),
        new ComposeSwitch("PostgreSQL 18", "Contenedor en localhost:5432.", "postgres"),
        new ComposeSwitch("MongoDB 9", "Contenedor en localhost:27017.", "mongo"),
        new ComposeSwitch("SQL Server 2025 (Docker)", "Contenedor en localhost,14330.", "mssql"),
        new ComposeSwitch("Redis 8", "Contenedor en localhost:6379. Cliente: docker exec -it devdb-redis-1 redis-cli", "redis"),
        new ComposeSwitch("Oracle 26ai Free", "Contenedor en localhost:1521. Usa hasta 3 GB.", "oracle"),
        new WslSwitch(),
        new EmulatorSwitch(),
    ];
}
