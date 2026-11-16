using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;

namespace DocumentProcessing.Infrastructure.Data;

/// <summary>
/// The pre-release migration history was squashed into <see cref="BaselineMigrationId"/>. Databases created from
/// the old history already have the baseline schema; this rewrites their history so EF does not try to recreate it.
/// </summary>
public static class MigrationBaseline
{
    public const string BaselineMigrationId = "20261116183700_InitialBaseline";

    /// <summary>The squashed migrations, in the order they were applied.</summary>
    public static readonly IReadOnlyList<string> LegacyMigrationIds =
    [
        "20261021123459_DescribeYourChanges",
        "20261021134312_InitialCreate",
        "20261021205052_SyncAppDbContextModel",
        "20261021233805_AddDocumentReview",
        "20261022050706_SyncCurrentModel",
        "20261116180249_AddAspNetCoreIdentity",
        "20261116181029_AddQueueCommandLeases",
        "20261116181849_WidenExtractionAndReviewColumns",
        "20261116182139_AddDocumentOwner",
        "20261116182322_UniqueReviewPerDocument"
    ];

    /// <summary>
    /// Replaces a complete legacy history with the baseline entry. Does nothing for empty or already-baselined
    /// databases; throws for a partially migrated legacy database, whose schema does not match the baseline.
    /// </summary>
    /// <returns><c>true</c> if the history was rewritten.</returns>
    public static async Task<bool> AdoptLegacyHistoryAsync(
        AppDbContext dbContext, ILogger logger, CancellationToken cancellationToken = default)
    {
        var applied = (await dbContext.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();
        if (applied.Contains(BaselineMigrationId))
            return false;

        var legacy = applied.Intersect(LegacyMigrationIds).ToList();
        if (legacy.Count == 0)
            return false;

        var missing = LegacyMigrationIds.Except(applied).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"The database is on a pre-baseline migration history that is missing {string.Join(", ", missing)}. " +
                "Upgrade it with a release from before the migration squash first, or recreate the database.");

        var legacyIds = LegacyMigrationIds.ToArray();
        var productVersion = ProductInfo.GetVersion();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.Database.ExecuteSqlAsync(
            $"""DELETE FROM "__EFMigrationsHistory" WHERE "MigrationId" = ANY({legacyIds})""", cancellationToken);
        await dbContext.Database.ExecuteSqlAsync(
            $"""INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion") VALUES ({BaselineMigrationId}, {productVersion})""",
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogWarning("Replaced {Count} legacy migration history entries with baseline {Baseline}.",
            legacyIds.Length, BaselineMigrationId);
        return true;
    }
}
