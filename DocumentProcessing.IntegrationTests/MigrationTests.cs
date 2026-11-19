using DocumentProcessing.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace DocumentProcessing.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class MigrationTests(PostgresFixture postgres)
{
    private static AppDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options);

    /// <summary>Brings a database to the baseline only, as a legacy database would be before adoption.</summary>
    private static Task MigrateToBaselineAsync(AppDbContext db) =>
        db.GetService<IMigrator>().MigrateAsync(MigrationBaseline.BaselineMigrationId);

    [Fact]
    public async Task Baseline_applies_to_an_empty_database_and_matches_the_model()
    {
        await using var db = CreateContext(await postgres.CreateDatabaseAsync());

        await db.Database.MigrateAsync();

        (await db.Database.GetAppliedMigrationsAsync()).Should().Equal(db.Database.GetMigrations())
            .And.StartWith(MigrationBaseline.BaselineMigrationId);
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        db.Database.HasPendingModelChanges().Should().BeFalse("the baseline must contain the whole current model");
    }

    [Fact]
    public async Task Baseline_enforces_one_review_per_document()
    {
        await using var db = CreateContext(await postgres.CreateDatabaseAsync());
        await db.Database.MigrateAsync();

        var indexes = await db.Database
            .SqlQuery<string>($"SELECT indexdef AS \"Value\" FROM pg_indexes WHERE tablename = 'DocumentReviews'")
            .ToListAsync();

        indexes.Should().Contain(i => i.Contains("UNIQUE") && i.Contains("\"DocumentId\""));
    }

    [Fact]
    public async Task Legacy_history_is_replaced_by_the_baseline()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var db = CreateContext(connectionString);
        await MigrateToBaselineAsync(db);
        await RewriteHistoryAsync(connectionString, MigrationBaseline.LegacyMigrationIds);

        var adopted = await MigrationBaseline.AdoptLegacyHistoryAsync(db, NullLogger.Instance);
        await db.Database.MigrateAsync();

        adopted.Should().BeTrue();
        (await db.Database.GetAppliedMigrationsAsync()).Should().Equal(db.Database.GetMigrations());
    }

    [Fact]
    public async Task Adoption_leaves_an_uncreated_database_for_MigrateAsync_to_create()
    {
        await using var db = CreateContext(postgres.UncreatedDatabaseConnectionString());

        (await MigrationBaseline.AdoptLegacyHistoryAsync(db, NullLogger.Instance)).Should().BeFalse();
        await db.Database.MigrateAsync();

        (await db.Database.GetAppliedMigrationsAsync()).Should().Equal(db.Database.GetMigrations());
    }

    [Fact]
    public async Task Concurrent_adoption_rewrites_the_history_exactly_once()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using (var setup = CreateContext(connectionString))
            await MigrateToBaselineAsync(setup);
        await RewriteHistoryAsync(connectionString, MigrationBaseline.LegacyMigrationIds);

        // Hold back history writes (reads still pass) so every instance reads the legacy history before any commits.
        await using var blocker = new NpgsqlConnection(connectionString);
        await blocker.OpenAsync();
        await using var blockerTx = await blocker.BeginTransactionAsync();
        await using (var lockTable = new NpgsqlCommand(
                         "LOCK TABLE \"__EFMigrationsHistory\" IN SHARE ROW EXCLUSIVE MODE", blocker, blockerTx))
            await lockTable.ExecuteNonQueryAsync();

        const int instances = 4;
        var adoptions = Enumerable.Range(0, instances).Select(_ => Task.Run(async () =>
        {
            await using var db = CreateContext(connectionString);
            return await MigrationBaseline.AdoptLegacyHistoryAsync(db, NullLogger.Instance);
        })).ToList();
        await WaitForBlockedSessionsAsync(connectionString, instances);
        await blockerTx.RollbackAsync();

        var results = await Task.WhenAll(adoptions);

        results.Count(adopted => adopted).Should().Be(1);
        await using var check = CreateContext(connectionString);
        (await check.Database.GetAppliedMigrationsAsync()).Should().Equal(MigrationBaseline.BaselineMigrationId);
    }

    [Fact]
    public async Task Adoption_is_a_no_op_for_empty_and_baselined_databases()
    {
        await using var db = CreateContext(await postgres.CreateDatabaseAsync());

        (await MigrationBaseline.AdoptLegacyHistoryAsync(db, NullLogger.Instance)).Should().BeFalse();
        await db.Database.MigrateAsync();
        (await MigrationBaseline.AdoptLegacyHistoryAsync(db, NullLogger.Instance)).Should().BeFalse();
    }

    [Fact]
    public async Task Partially_migrated_legacy_database_is_refused()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var db = CreateContext(connectionString);
        await db.GetService<IMigrator>().MigrateAsync(MigrationBaseline.BaselineMigrationId);
        await RewriteHistoryAsync(connectionString, MigrationBaseline.LegacyMigrationIds.Take(5));

        var act = () => MigrationBaseline.AdoptLegacyHistoryAsync(db, NullLogger.Instance);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*AddAspNetCoreIdentity*");
    }

    private static async Task WaitForBlockedSessionsAsync(string connectionString, int expected)
    {
        // Own connection: pg_stat_activity is snapshotted per transaction.
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var count = new NpgsqlCommand(
            "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'",
            connection);
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if ((long)(await count.ExecuteScalarAsync())! >= expected)
                return;
            await Task.Delay(100);
        }
        throw new TimeoutException($"Expected {expected} sessions waiting on locks.");
    }

    private static async Task RewriteHistoryAsync(string connectionString, IEnumerable<string> migrationIds)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var delete = new NpgsqlCommand("DELETE FROM \"__EFMigrationsHistory\"", connection))
            await delete.ExecuteNonQueryAsync();
        foreach (var id in migrationIds)
        {
            await using var insert = new NpgsqlCommand(
                "INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES (@id, '10.0.0')", connection);
            insert.Parameters.AddWithValue("id", id);
            await insert.ExecuteNonQueryAsync();
        }
    }
}
