using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using DocumentProcessing.Application.Services.Invoice;

namespace DocumentProcessing.Application.Services;

/// <summary>
/// The reviewable invoice fields: their string form in the review API, and how reviewer corrections
/// are checked and applied on top of the extraction result.
/// </summary>
public static partial class ReviewFields
{
    public const int MaxValueLength = 1_000;
    public const int MaxLinesLength = 20_000;
    private const string DateFormat = "yyyy-MM-dd";

    private enum Kind { Text, Date, Amount, Currency, Lines }

    private static readonly (string Name, Kind Kind)[] Fields =
    {
        ("SupplierName", Kind.Text),
        ("InvoiceNumber", Kind.Text),
        ("InvoiceDate", Kind.Date),
        ("DueDate", Kind.Date),
        ("Currency", Kind.Currency),
        ("Subtotal", Kind.Amount),
        ("TaxAmount", Kind.Amount),
        ("TotalAmount", Kind.Amount),
        ("Lines", Kind.Lines)
    };

    public static IReadOnlyList<string> Names { get; } = Fields.Select(f => f.Name).ToArray();

    /// <summary>
    /// The field values as the review API shows them; null when a field has no value.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> ToValues(InvoiceExtractionResult r) => new Dictionary<string, string?>
    {
        ["SupplierName"] = NullIfBlank(r.SupplierName),
        ["InvoiceNumber"] = NullIfBlank(r.InvoiceNumber),
        ["InvoiceDate"] = r.InvoiceDate?.ToString(DateFormat, CultureInfo.InvariantCulture),
        ["DueDate"] = r.DueDate?.ToString(DateFormat, CultureInfo.InvariantCulture),
        ["Currency"] = NullIfBlank(r.Currency),
        ["Subtotal"] = r.Subtotal?.ToString("0.00", CultureInfo.InvariantCulture),
        ["TaxAmount"] = r.TaxAmount?.ToString("0.00", CultureInfo.InvariantCulture),
        ["TotalAmount"] = r.TotalAmount?.ToString("0.00", CultureInfo.InvariantCulture),
        ["Lines"] = r.Lines is { Count: > 0 } ? string.Join("\n", r.Lines) : null
    };

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// Checks reviewer input: known field names, length limits, and dates (yyyy-MM-dd), amounts and ISO currency codes
    /// in a parseable form. An empty value clears the field.
    /// </summary>
    public static IReadOnlyList<string> Validate(IDictionary<string, string>? corrections)
    {
        var errors = new List<string>();
        if (corrections == null)
            return errors;

        foreach (var (name, value) in corrections)
        {
            var field = Fields.FirstOrDefault(f => f.Name == name);
            if (field.Name == null)
            {
                errors.Add($"Unknown field '{name}'. Allowed fields: {string.Join(", ", Names)}.");
                continue;
            }

            var text = value?.Trim() ?? string.Empty;
            var maxLength = field.Kind == Kind.Lines ? MaxLinesLength : MaxValueLength;
            if (text.Length > maxLength)
            {
                errors.Add($"Field '{name}' is longer than {maxLength} characters.");
                continue;
            }
            if (text.Length == 0)
                continue;

            var valid = field.Kind switch
            {
                Kind.Date => DateTime.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
                Kind.Amount => TryParseAmount(text, out _),
                Kind.Currency => CurrencyCode().IsMatch(text),
                _ => true
            };
            if (!valid)
            {
                errors.Add(field.Kind switch
                {
                    Kind.Date => $"Field '{name}' must be a date in the form yyyy-MM-dd.",
                    Kind.Amount => $"Field '{name}' must be a number such as 1234.56.",
                    _ => $"Field '{name}' must be a three-letter currency code such as EUR."
                });
            }
        }
        return errors;
    }

    /// <summary>
    /// Applies validated corrections to the extraction result in place. Empty values clear a field.
    /// </summary>
    public static InvoiceExtractionResult Apply(InvoiceExtractionResult r, IReadOnlyDictionary<string, string> corrections)
    {
        foreach (var (name, raw) in corrections)
        {
            var text = raw?.Trim() ?? string.Empty;
            var has = text.Length > 0;
            switch (name)
            {
                case "SupplierName": r.SupplierName = has ? text : null; r.SupplierNameHasValue = has; break;
                case "InvoiceNumber": r.InvoiceNumber = has ? text : null; r.InvoiceNumberHasValue = has; break;
                case "InvoiceDate": r.InvoiceDate = ParseDate(text); r.InvoiceDateHasValue = r.InvoiceDate.HasValue; break;
                case "DueDate": r.DueDate = ParseDate(text); r.DueDateHasValue = r.DueDate.HasValue; break;
                case "Currency": r.Currency = has ? text.ToUpperInvariant() : null; r.CurrencyHasValue = has; break;
                case "Subtotal": r.Subtotal = ParseAmount(text); r.SubtotalHasValue = r.Subtotal.HasValue; break;
                case "TaxAmount": r.TaxAmount = ParseAmount(text); r.TaxAmountHasValue = r.TaxAmount.HasValue; break;
                case "TotalAmount": r.TotalAmount = ParseAmount(text); r.TotalAmountHasValue = r.TotalAmount.HasValue; break;
                case "Lines":
                    r.Lines = has ? SplitLines(text) : null;
                    r.LinesHasValue = r.Lines is { Count: > 0 };
                    break;
            }
        }
        return r;
    }

    /// <summary>
    /// Reads a stored corrections/approved-values JSON object; anything else (e.g. the original AI output) yields no values.
    /// </summary>
    public static Dictionary<string, string> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return new Dictionary<string, string>();
            return doc.RootElement.EnumerateObject()
                .Where(p => Names.Contains(p.Name) && p.Value.ValueKind is JsonValueKind.String or JsonValueKind.Null)
                .ToDictionary(p => p.Name, p => p.Value.GetString() ?? string.Empty);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }

    public static List<string> SplitLines(string? text) =>
        string.IsNullOrEmpty(text)
            ? new List<string>()
            : text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0).ToList();

    private static DateTime? ParseDate(string text) =>
        DateTime.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    private static decimal? ParseAmount(string text) => TryParseAmount(text, out var amount) ? amount : null;

    private static bool TryParseAmount(string text, out decimal amount) =>
        decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount);

    [GeneratedRegex("^[A-Za-z]{3}$")]
    private static partial Regex CurrencyCode();
}
