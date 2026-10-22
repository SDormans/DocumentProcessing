using System.Text.Json.Serialization;

namespace DocumentProcessing.Application.Services.Invoice;

/// <summary>
/// Result of a single invoice validation check.
/// </summary>
public class ValidationResult
{
    /// <summary>
    /// Unique identifier of the rule (e.g. "MISSING_REQUIRED_FIELDS", "INVALID_DATE").
    /// </summary>
    [JsonPropertyName("ruleId")]
    public string RuleId { get; set; } = string.Empty;

    /// <summary>
    /// Status of the validation check: Passing, Failing, Warning, Info.
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = "Info";

    /// <summary>
    /// Severity of the issue: Critical, Error, Warning, Info.
    /// </summary>
    [JsonPropertyName("severity")]
    public string Severity { get; set; } = "Info";

    /// <summary>
    /// Human-readable explanation of the issue.
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Field(s) affected by the validation issue.
    /// </summary>
    [JsonPropertyName("affectedFields")]
    public string? AffectedFields { get; set; }

    /// <summary>
    /// Optional supplementary contextual data.
    /// </summary>
    [JsonPropertyName("extraData")]
    public object? ExtraData { get; set; }
}