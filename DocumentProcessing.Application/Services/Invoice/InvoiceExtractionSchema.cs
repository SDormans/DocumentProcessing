using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DocumentProcessing.Application.Services.Invoice;

/// <summary>
/// JSON schema the LLM must conform to (OpenAI "strict" structured outputs compatible) and the
/// parser that maps a conforming response onto <see cref="InvoiceExtractionResult"/>.
/// </summary>
public static class InvoiceExtractionSchema
{
    public const string Name = "invoice_extraction";

    private static readonly string[] TopLevelFields =
    [
        "supplierName", "invoiceNumber", "invoiceDate", "dueDate", "currency",
        "subtotal", "taxAmount", "totalAmount", "lines", "warnings"
    ];

    private static readonly string[] LineFields = ["description", "quantity", "unitPrice", "amount"];

    public static JsonObject CreateSchema()
    {
        static JsonObject Nullable(string type, string description) => new()
        {
            ["type"] = new JsonArray(type, "null"),
            ["description"] = description
        };

        var lineProperties = new JsonObject
        {
            ["description"] = new JsonObject { ["type"] = "string" },
            ["quantity"] = Nullable("number", "Quantity, or null if not printed."),
            ["unitPrice"] = Nullable("number", "Unit price excluding tax, or null."),
            ["amount"] = Nullable("number", "Line total excluding tax, or null.")
        };

        var properties = new JsonObject
        {
            ["supplierName"] = Nullable("string", "Legal name of the issuing supplier."),
            ["invoiceNumber"] = Nullable("string", "Invoice number exactly as printed."),
            ["invoiceDate"] = Nullable("string", "Invoice date as YYYY-MM-DD."),
            ["dueDate"] = Nullable("string", "Payment due date as YYYY-MM-DD."),
            ["currency"] = Nullable("string", "ISO 4217 currency code, e.g. EUR."),
            ["subtotal"] = Nullable("number", "Total excluding tax."),
            ["taxAmount"] = Nullable("number", "Total tax (VAT) amount."),
            ["totalAmount"] = Nullable("number", "Total including tax."),
            ["lines"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = lineProperties,
                    ["required"] = new JsonArray(LineFields.Select(f => (JsonNode)f).ToArray()),
                    ["additionalProperties"] = false
                }
            },
            ["warnings"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject { ["type"] = "string" },
                ["description"] = "Ambiguities or values that could not be read reliably."
            }
        };

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = new JsonArray(TopLevelFields.Select(f => (JsonNode)f).ToArray()),
            ["additionalProperties"] = false
        };
    }

    /// <summary>
    /// Parses and validates a schema-conforming JSON document. Returns a failure result when the
    /// payload is not valid JSON or does not match the schema.
    /// </summary>
    public static InvoiceExtractionResult Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return InvoiceExtractionResult.FailureResult($"AI response is not valid JSON: {ex.Message}", json);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return InvoiceExtractionResult.FailureResult("AI response is not a JSON object.", json);

            var errors = new List<string>();
            foreach (var field in TopLevelFields)
            {
                if (!root.TryGetProperty(field, out _))
                    errors.Add($"Missing required field '{field}'.");
            }
            if (errors.Count > 0)
                return InvoiceExtractionResult.FailureResult(string.Join(" ", errors), json);

            var data = new InvoiceExtractionResult { ExtractionMethod = "Llm" };

            data.SupplierName = ReadString(root, "supplierName", errors);
            data.InvoiceNumber = ReadString(root, "invoiceNumber", errors);
            data.InvoiceDate = ReadDate(root, "invoiceDate", errors, data.Warnings);
            data.DueDate = ReadDate(root, "dueDate", errors, data.Warnings);
            data.Currency = ReadString(root, "currency", errors)?.Trim().ToUpperInvariant();
            data.Subtotal = ReadDecimal(root, "subtotal", errors);
            data.TaxAmount = ReadDecimal(root, "taxAmount", errors);
            data.TotalAmount = ReadDecimal(root, "totalAmount", errors);
            data.Lines = ReadLines(root.GetProperty("lines"), errors);

            var warnings = root.GetProperty("warnings");
            if (warnings.ValueKind == JsonValueKind.Array)
                data.Warnings.AddRange(warnings.EnumerateArray()
                    .Where(w => w.ValueKind == JsonValueKind.String)
                    .Select(w => w.GetString()!)
                    .Where(w => !string.IsNullOrWhiteSpace(w)));
            else
                errors.Add("Field 'warnings' must be an array.");

            if (errors.Count > 0)
                return InvoiceExtractionResult.FailureResult(string.Join(" ", errors), json);

            data.SupplierNameHasValue = !string.IsNullOrWhiteSpace(data.SupplierName);
            data.InvoiceNumberHasValue = !string.IsNullOrWhiteSpace(data.InvoiceNumber);
            data.InvoiceDateHasValue = data.InvoiceDate.HasValue;
            data.DueDateHasValue = data.DueDate.HasValue;
            data.CurrencyHasValue = !string.IsNullOrWhiteSpace(data.Currency);
            data.SubtotalHasValue = data.Subtotal.HasValue;
            data.TaxAmountHasValue = data.TaxAmount.HasValue;
            data.TotalAmountHasValue = data.TotalAmount.HasValue;
            data.LinesHasValue = data.Lines is { Count: > 0 };

            return InvoiceExtractionResult.SuccessResult(json, data);
        }
    }

    private static string? ReadString(JsonElement root, string name, List<string> errors)
    {
        var value = root.GetProperty(name);
        switch (value.ValueKind)
        {
            case JsonValueKind.Null:
                return null;
            case JsonValueKind.String:
                var text = value.GetString();
                return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            default:
                errors.Add($"Field '{name}' must be a string or null.");
                return null;
        }
    }

    private static decimal? ReadDecimal(JsonElement root, string name, List<string> errors)
    {
        var value = root.GetProperty(name);
        if (value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
            return number;
        errors.Add($"Field '{name}' must be a number or null.");
        return null;
    }

    private static DateTime? ReadDate(JsonElement root, string name, List<string> errors, List<string> warnings)
    {
        var text = ReadString(root, name, errors);
        if (text is null)
            return null;
        if (DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var date))
            return DateTime.SpecifyKind(date, DateTimeKind.Utc);
        warnings.Add($"Field '{name}' value '{text}' is not a valid YYYY-MM-DD date and was ignored.");
        return null;
    }

    private static List<string> ReadLines(JsonElement lines, List<string> errors)
    {
        var result = new List<string>();
        if (lines.ValueKind != JsonValueKind.Array)
        {
            errors.Add("Field 'lines' must be an array.");
            return result;
        }

        var index = 0;
        foreach (var line in lines.EnumerateArray())
        {
            if (line.ValueKind != JsonValueKind.Object || LineFields.Any(f => !line.TryGetProperty(f, out _)))
            {
                errors.Add($"Line {index} does not match the line schema.");
                index++;
                continue;
            }

            var description = ReadString(line, "description", errors) ?? string.Empty;
            var quantity = ReadDecimal(line, "quantity", errors);
            var unitPrice = ReadDecimal(line, "unitPrice", errors);
            var amount = ReadDecimal(line, "amount", errors);
            result.Add(FormatLine(description, quantity, unitPrice, amount));
            index++;
        }

        return result;
    }

    private static string FormatLine(string description, decimal? quantity, decimal? unitPrice, decimal? amount)
    {
        var parts = new List<string> { description };
        if (quantity.HasValue || unitPrice.HasValue)
            parts.Add($"{Format(quantity)} x {Format(unitPrice)}");
        if (amount.HasValue)
            parts.Add($"= {Format(amount)}");
        return string.Join(" | ", parts);
    }

    private static string Format(decimal? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? "?";
}
