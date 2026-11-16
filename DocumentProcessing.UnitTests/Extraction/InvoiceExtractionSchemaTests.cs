using DocumentProcessing.Application.Services.Invoice;
using FluentAssertions;
using Xunit;

namespace DocumentProcessing.UnitTests.Extraction;

public class InvoiceExtractionSchemaTests
{
    public const string ValidJson = """
        {
          "supplierName": "Acme B.V.",
          "invoiceNumber": "INV-2026-001",
          "invoiceDate": "2026-10-01",
          "dueDate": "2026-10-31",
          "currency": "eur",
          "subtotal": 100.00,
          "taxAmount": 21.00,
          "totalAmount": 121.00,
          "lines": [ { "description": "Consulting", "quantity": 2, "unitPrice": 50, "amount": 100 } ],
          "warnings": []
        }
        """;

    [Fact]
    public void Parse_ValidPayload_MapsAllFields()
    {
        var result = InvoiceExtractionSchema.Parse(ValidJson);

        result.Success.Should().BeTrue();
        result.SupplierName.Should().Be("Acme B.V.");
        result.InvoiceNumberHasValue.Should().BeTrue();
        result.InvoiceDate.Should().Be(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
        result.Currency.Should().Be("EUR");
        result.TotalAmount.Should().Be(121.00m);
        result.Lines.Should().ContainSingle().Which.Should().Be("Consulting | 2 x 50 | = 100");
        result.RawResponse.Should().Be(ValidJson);
    }

    [Fact]
    public void Parse_NullValues_SetHasValueFalse()
    {
        var json = ValidJson.Replace("\"Acme B.V.\"", "null").Replace("121.00", "null");

        var result = InvoiceExtractionSchema.Parse(json);

        result.Success.Should().BeTrue();
        result.SupplierNameHasValue.Should().BeFalse();
        result.TotalAmountHasValue.Should().BeFalse();
    }

    [Fact]
    public void Parse_MissingRequiredField_Fails()
    {
        var json = ValidJson.Replace("\"invoiceNumber\": \"INV-2026-001\",", string.Empty);

        var result = InvoiceExtractionSchema.Parse(json);

        result.Success.Should().BeFalse();
        result.Warnings.Should().ContainSingle().Which.Should().Contain("invoiceNumber");
    }

    [Fact]
    public void Parse_WrongType_Fails()
    {
        var result = InvoiceExtractionSchema.Parse(ValidJson.Replace("121.00", "\"121,00\""));

        result.Success.Should().BeFalse();
        result.Warnings.Should().ContainSingle().Which.Should().Contain("totalAmount");
    }

    [Fact]
    public void Parse_InvalidDate_IsDroppedWithWarning()
    {
        var result = InvoiceExtractionSchema.Parse(ValidJson.Replace("2026-10-31", "31/10/2026"));

        result.Success.Should().BeTrue();
        result.DueDateHasValue.Should().BeFalse();
        result.Warnings.Should().Contain(w => w.Contains("dueDate"));
    }

    [Theory]
    [InlineData("\"warnings\": []", "\"warnings\": [1]")]
    [InlineData("\"warnings\": []", "\"warnings\": [], \"extra\": 1")]
    [InlineData("\"description\": \"Consulting\"", "\"description\": null")]
    [InlineData("\"amount\": 100 }", "\"amount\": 100, \"vat\": 21 }")]
    public void Parse_SchemaViolations_Fail(string original, string replacement)
    {
        var result = InvoiceExtractionSchema.Parse(ValidJson.Replace(original, replacement));

        result.Success.Should().BeFalse();
    }

    [Fact]
    public void Parse_NotJson_Fails()
    {
        InvoiceExtractionSchema.Parse("not json").Success.Should().BeFalse();
    }

    [Fact]
    public void Schema_IsStrictCompatible()
    {
        var schema = InvoiceExtractionSchema.CreateSchema();

        schema["additionalProperties"]!.GetValue<bool>().Should().BeFalse();
        schema["required"]!.AsArray().Count.Should().Be(schema["properties"]!.AsObject().Count);
    }
}
