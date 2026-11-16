using DocumentProcessing.Application.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DocumentProcessing.UnitTests.Services;

public class DocumentAccessOptionsTests
{
    private static DocumentAccessOptions Bind(Dictionary<string, string?> settings)
    {
        var options = new DocumentAccessOptions();
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build()
            .GetSection(DocumentAccessOptions.SectionName).Bind(options);
        return options;
    }

    [Fact]
    public void Defaults_to_admin_when_not_configured()
    {
        Bind(new()).EffectiveUnrestrictedRoles.Should().Equal("Admin");
    }

    [Fact]
    public void Configured_roles_replace_the_default()
    {
        var options = Bind(new() { ["DocumentAccess:UnrestrictedRoles:0"] = "Auditor" });

        options.EffectiveUnrestrictedRoles.Should().Equal("Auditor");
    }
}
