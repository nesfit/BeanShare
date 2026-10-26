using BeanShare.Domain.Common;
using BeanShare.Infrastructure.Persistence.Seeds;
using BeanShare.Infrastructure.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeanShare.Infrastructure.Persistence;

public static class DatabaseInitializationExtensions
{
    private const long PostgreSqlMigrationLockId = 7_305_202_601_330;

    public static async Task InitializeBeanShareDatabaseAsync(
        this IServiceProvider serviceProvider,
        bool includeDemoData = false,
        CancellationToken cancellationToken = default)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BeanShareDbContext>();

        if (context.Database.IsRelational())
        {
            var connection = context.Database.GetDbConnection();
            await connection.OpenAsync(cancellationToken);

            var usePostgreSqlLock = string.Equals(
                context.Database.ProviderName,
                "Npgsql.EntityFrameworkCore.PostgreSQL",
                StringComparison.Ordinal);

            if (usePostgreSqlLock)
            {
                await using var lockCommand = connection.CreateCommand();
                lockCommand.CommandText = $"SELECT pg_advisory_lock({PostgreSqlMigrationLockId})";
                await lockCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            try
            {
                await context.Database.MigrateAsync(cancellationToken);
                await RunLotBackfillAsync(scope.ServiceProvider, context, cancellationToken);
                await SeedAsync(scope.ServiceProvider, context, includeDemoData, cancellationToken);
                await RunLedgerBackfillAsync(scope.ServiceProvider, context, cancellationToken);
            }
            finally
            {
                if (usePostgreSqlLock)
                {
                    await using var unlockCommand = connection.CreateCommand();
                    unlockCommand.CommandText = $"SELECT pg_advisory_unlock({PostgreSqlMigrationLockId})";
                    await unlockCommand.ExecuteNonQueryAsync(cancellationToken);
                }

                await connection.CloseAsync();
            }

            return;
        }

        await context.Database.EnsureCreatedAsync(cancellationToken);
        await SeedAsync(scope.ServiceProvider, context, includeDemoData, cancellationToken);
        await RunLedgerBackfillAsync(scope.ServiceProvider, context, cancellationToken);
    }

    private static async Task SeedAsync(
        IServiceProvider serviceProvider,
        BeanShareDbContext context,
        bool includeDemoData,
        CancellationToken cancellationToken)
    {
        var logger = serviceProvider.GetRequiredService<ILogger<DatabaseSeeder>>();
        var seeder = new DatabaseSeeder(context, logger);
        await seeder.SeedAsync(includeDemoData: includeDemoData, cancellationToken);
    }

    /// <summary>Stamps cups already billed before the money ledger and freezes legacy Paid (idempotent).</summary>
    private static async Task RunLedgerBackfillAsync(
        IServiceProvider serviceProvider,
        BeanShareDbContext context,
        CancellationToken cancellationToken)
    {
        var logger = serviceProvider.GetRequiredService<ILogger<LedgerBackfillService>>();
        await new LedgerBackfillService(context, logger).RunAsync(cancellationToken);
    }

    /// <summary>
    /// One-time reconstruction of per-bag state from pre-per-bag data (idempotent; runs before seeding).
    /// </summary>
    private static async Task RunLotBackfillAsync(
        IServiceProvider serviceProvider,
        BeanShareDbContext context,
        CancellationToken cancellationToken)
    {
        var clock = serviceProvider.GetRequiredService<IClock>();
        var logger = serviceProvider.GetRequiredService<ILogger<LotBackfillService>>();
        var backfill = new LotBackfillService(context, clock, logger);
        await backfill.RunAsync(cancellationToken);
    }
}
