namespace DocumentProcessing.Application.Services.Invoice;

/// <summary>
/// Possible statuses for an invoice validation result.
/// </summary>
public enum ValidationStatus
{
    /// <summary>
    /// Validation passed without errors or warnings.
    /// </summary>
    Passing,

    /// <summary>
    /// An error was found that requires attention.
    /// </summary>
    Failing,

    /// <summary>
    /// A warning was found that affects quality but does not invalidate the document.
    /// </summary>
    Warning,

    /// <summary>
    /// Informational message, neither error nor warning.
    /// </summary>
    Info
}

/// <summary>
/// Severity levels for invoice validation issues.
/// </summary>
public enum ValidationSeverity
{
    /// <summary>
    /// Critical issue that prevents automated processing.
    /// </summary>
    Critical,

    /// <summary>
    /// Incorrect or untrustworthy data.
    /// </summary>
    Error,

    /// <summary>
    /// Potential issue that should be reviewed.
    /// </summary>
    Warning,

    /// <summary>
    /// Informational detail.
    /// </summary>
    Info
}