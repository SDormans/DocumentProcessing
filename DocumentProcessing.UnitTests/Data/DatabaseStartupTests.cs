using DocumentProcessing.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace DocumentProcessing.UnitTests.Data;

public class DatabaseStartupTests
{
    [Fact]
    public async Task WaitForServerAsync_GivesUpWithServerAddress_WhenNothingListens()
    {
        // Port 1 on loopback has no listener, so the connection is refused immediately.
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused;Timeout=2")
            .Options);
        var options = new DatabaseStartupOptions { ConnectAttempts = 2, ConnectRetryDelay = TimeSpan.FromMilliseconds(10) };

        var act = () => DatabaseStartup.WaitForServerAsync(db, options, NullLogger.Instance);

        var error = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
        error.Message.Should().Contain("127.0.0.1:1").And.Contain("after 2 attempt(s)");
        error.InnerException.Should().BeAssignableTo<NpgsqlException>();
    }

    [Fact]
    public async Task WaitForServerAsync_StopsRetrying_WhenCancelled()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused;Timeout=2")
            .Options);
        var options = new DatabaseStartupOptions { ConnectAttempts = 100, ConnectRetryDelay = TimeSpan.FromMinutes(1) };
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        var act = () => DatabaseStartup.WaitForServerAsync(db, options, NullLogger.Instance, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
