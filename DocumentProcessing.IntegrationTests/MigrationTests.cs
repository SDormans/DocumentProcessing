using DocumentProcessing.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace DocumentProcessing.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class MigrationTests(PostgresFixture postgres)
{
    private static AppDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options);

    [Fact]
    public async Task Baseline_applies_to_an_empty_database_and_matches_the_model()
    {
        await using var db = CreateContext(await postgres.CreateDatabaseAsync());

        await db.Database.MigrateAsync();

        (await db.Database.GetAppliedMigrationsAsync()).Should().Equal(MigrationBaseline.BaselineMigrationId);
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
        await db.Database.MigrateAsync();
        await RewriteHistoryAsync(connectionString, MigrationBaseline.LegacyMigrationIds);

        var adopted = await MigrationBaseline.AdoptLegacyHistoryAsync(db, NullLogger.Instance);
        await db.Database.MigrateAsync();

        adopted.Should().BeTrue();
        (await db.Database.GetAppliedMigrationsAsync()).Should().Equal(MigrationBaseline.BaselineMigrationId);
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
        await db.Database.MigrateAsync();
        await RewriteHistoryAsync(connectionString, MigrationBaseline.LegacyMigrationIds.Take(5));

        var act = () => MigrationBaseline.AdoptLegacyHistoryAsync(db, NullLogger.Instance);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*AddAspNetCoreIdentity*");
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
