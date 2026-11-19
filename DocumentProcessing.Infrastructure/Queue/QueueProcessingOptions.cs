namespace DocumentProcessing.Infrastructure.Queue;

public class QueueProcessingOptions
{
    public const string SectionName = "QueueProcessing";

    /// <summary>Maximum number of commands a worker processes at the same time.</summary>
    public int BatchSize { get; set; } = 4;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long a claim is valid without renewal. The worker renews it every <see cref="LeaseRenewalInterval"/>
    /// while processing, so it only has to outlive a crashed worker, not the slowest document.
    /// </summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How often a processing worker extends its lease. Defaults to a third of <see cref="LeaseDuration"/>.</summary>
    public TimeSpan? LeaseRenewalInterval { get; set; }

    public TimeSpan EffectiveLeaseRenewalInterval => LeaseRenewalInterval is { } interval && interval > TimeSpan.Zero && interval < LeaseDuration
        ? interval
        : LeaseDuration / 3;

    public int MaxAttempts { get; set; } = 3;

    /// <summary>Delay before the first retry; doubles per attempt.</summary>
    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How long completed commands are kept before the worker deletes them. Failed commands are kept.</summary>
    public TimeSpan CompletedRetention { get; set; } = TimeSpan.FromDays(30);

    /// <summary>How often the worker purges completed commands.</summary>
    public TimeSpan PurgeInterval { get; set; } = TimeSpan.FromHours(1);
}
