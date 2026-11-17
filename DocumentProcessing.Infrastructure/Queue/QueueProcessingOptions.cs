namespace DocumentProcessing.Infrastructure.Queue;

public class QueueProcessingOptions
{
    public const string SectionName = "QueueProcessing";

    /// <summary>Commands claimed (and processed in parallel) per poll.</summary>
    public int BatchSize { get; set; } = 4;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long a claim is valid. Must exceed the worst-case processing time (AI total timeout
    /// included); an expired lease lets another worker reclaim the command.
    /// </summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);

    public int MaxAttempts { get; set; } = 3;

    /// <summary>Delay before the first retry; doubles per attempt.</summary>
    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How long completed commands are kept before the worker deletes them. Failed commands are kept.</summary>
    public TimeSpan CompletedRetention { get; set; } = TimeSpan.FromDays(30);

    /// <summary>How often the worker purges completed commands.</summary>
    public TimeSpan PurgeInterval { get; set; } = TimeSpan.FromHours(1);
}
