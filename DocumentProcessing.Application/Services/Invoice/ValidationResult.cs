using System.Text.Json.Serialization;

namespace DocumentProcessing.Application.Services.Invoice;

/// <summary>
/// Resultaat van een enkele validatieregel.
/// </summary>
public class ValidationResult
{
    /// <summary>
    /// Unieke identifier van de regel (bijv. "MISSING_INVOICE_NUMBER", "INVALID_DATE")
    /// </summary>
    [JsonPropertyName("ruleId")]
    public string RuleId { get; set; } = string.Empty;

    /// <summary>
    /// Status van de validatie: Passing, Failing, Warning, Info
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = "Info";

    /// <summary>
    /// Ernst van het probleem: Critical, Error, Warning, Info
    /// </summary>
    [JsonPropertyName("severity")]
    public string Severity { get; set; } = "Info";

    /// <summary>
    /// Menselijke leesbare beschrijving van het probleem
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Betrokken velden bij het probleem (naam van het veld of lijst van velden)
    /// </summary>
    [JsonPropertyName("affectedFields")]
    public string? AffectedFields { get; set; }

    /// <summary>
    /// Optionele extra contextgegevens (waarde die gevonden is, expectation, etc.)
    /// </summary>
    [JsonPropertyName("extraData")]
    public object? ExtraData { get; set; }
}