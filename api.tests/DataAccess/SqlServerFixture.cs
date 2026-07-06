using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Dac;
using Testcontainers.MsSql;

namespace Api.Tests.DataAccess;

/// <summary>
/// Starts one SQL Server container for the whole test collection and deploys
/// the real schema from the database project's DACPAC, so data-access tests
/// exercise the actual tables and constraints rather than a substitute.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private const string DatabaseName = "IntegrationDashboardTests";

    private readonly MsSqlContainer _container =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public string ConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var dacServices = new DacServices(_container.GetConnectionString());
        using var package = DacPackage.Load(FindOrBuildDacpac());
        dacServices.Deploy(package, DatabaseName, upgradeExisting: true);

        ConnectionString = new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = DatabaseName,
        }.ConnectionString;
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    private static string FindOrBuildDacpac()
    {
        var databaseDir = FindDatabaseProjectDirectory();

        var dacpac = LocateDacpac(databaseDir);
        if (dacpac is not null)
        {
            return dacpac;
        }

        // Not built yet (fresh clone / CI): build the sqlproj once.
        var build = Process.Start(new ProcessStartInfo("dotnet", "build")
        {
            WorkingDirectory = databaseDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException("Failed to start 'dotnet build' for the database project.");
        var output = build.StandardOutput.ReadToEnd() + build.StandardError.ReadToEnd();
        build.WaitForExit();
        if (build.ExitCode != 0)
        {
            throw new InvalidOperationException($"Building the database project failed:\n{output}");
        }

        return LocateDacpac(databaseDir)
            ?? throw new InvalidOperationException(
                $"DACPAC not found under {databaseDir} even after building the database project.");
    }

    private static string FindDatabaseProjectDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "database");
            if (File.Exists(Path.Combine(candidate, "IntegrationDashboard.Database.sqlproj")))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            "Could not locate the database project directory above " + AppContext.BaseDirectory);
    }

    private static string? LocateDacpac(string databaseDir)
    {
        var binDir = Path.Combine(databaseDir, "bin");
        if (!Directory.Exists(binDir))
        {
            return null;
        }
        return Directory
            .GetFiles(binDir, "IntegrationDashboard.Database.dacpac", SearchOption.AllDirectories)
            .FirstOrDefault();
    }
}

[CollectionDefinition("SqlServerDatabase")]
public class SqlServerDatabaseCollection : ICollectionFixture<SqlServerFixture>;
