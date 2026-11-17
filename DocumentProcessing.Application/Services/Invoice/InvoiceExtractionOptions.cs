namespace DocumentProcessing.Application.Services.Invoice;

public class InvoiceExtractionOptions
{
    public const string SectionName = "Ai";

    /// <summary>
    /// Per-document AI cost budget in the currency of the configured token prices. When the first
    /// pass reaches it, the correction pass is skipped. Null means no budget.
    /// </summary>
    public decimal? MaxCostPerDocument { get; set; }
}
