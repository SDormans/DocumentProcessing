using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace DocumentProcessing.Infrastructure.Data;

public class DatabaseStartupOptions
{
    public const string SectionName = "Database";

    /// <summary>Connection attempts before startup gives up on an unreachable PostgreSQL server.</summary>
    public int ConnectAttempts { get; set; } = 10;

    public TimeSpan ConnectRetryDelay { get; set; } = TimeSpan.FromSeconds(3);
}

public static class DatabaseStartup
{
    /// <summary>
    /// Waits until the PostgreSQL server accepts connections, retrying transient (network) failures.
    /// Any other server reply, such as a missing database, counts as reachable and is left to the migrations.
    /// </summary>
    public static async Task WaitForServerAsync(
        DbContext dbContext, DatabaseStartupOptions options, ILogger logger, CancellationToken cancellationToken = default)
    {
        var connectionString = dbContext.Database.GetConnectionString();
        var attempts = Math.Max(1, options.ConnectAttempts);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                // A plain connection, not EF's, whose execution strategy wraps transient errors.
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken);
                return;
            }
            catch (NpgsqlException ex) when (ex.IsTransient)
            {
                if (attempt >= attempts)
                    throw new InvalidOperationException(
                        $"Could not connect to PostgreSQL at {DescribeServer(connectionString)} after {attempts} attempt(s). " +
                        "Check that the server is running and that ConnectionStrings:DefaultConnection has the right host and port.",
                        ex);

                logger.LogWarning(
                    "PostgreSQL at {Server} is not reachable (attempt {Attempt}/{Attempts}): {Error}. Retrying in {Delay}.",
                    DescribeServer(connectionString), attempt, attempts, ex.Message, options.ConnectRetryDelay);
                await Task.Delay(options.ConnectRetryDelay, cancellationToken);
            }
            catch (PostgresException)
            {
                return;
            }
        }
    }

    private static string DescribeServer(string? connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        return $"{builder.Host}:{builder.Port}";
    }
}
