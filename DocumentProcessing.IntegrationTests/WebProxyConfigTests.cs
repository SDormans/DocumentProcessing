using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace DocumentProcessing.IntegrationTests;

/// <summary>
/// The web image's Nginx must forward uploads as large as the API accepts.
/// </summary>
public class WebProxyConfigTests
{
    private const long ApiMaxFileSizeBytes = 10 * 1024 * 1024;
    private const long MultipartAllowanceBytes = 128 * 1024;

    [Fact]
    public void Nginx_body_limit_is_configurable_and_defaults_above_the_api_upload_limit()
    {
        var web = Path.Combine(RepoRoot(), "DocumentProcessing.Web");
        File.ReadAllText(Path.Combine(web, "templates", "default.conf.template"))
            .Should().Contain("client_max_body_size ${CLIENT_MAX_BODY_SIZE};");

        var dockerfileDefault = Regex.Match(File.ReadAllText(Path.Combine(web, "Dockerfile")), @"CLIENT_MAX_BODY_SIZE=(\d+)m");
        var composeDefault = Regex.Match(File.ReadAllText(Path.Combine(RepoRoot(), "docker-compose.yml")), @"CLIENT_MAX_BODY_SIZE:-(\d+)m");

        foreach (var match in new[] { dockerfileDefault, composeDefault })
        {
            match.Success.Should().BeTrue();
            (long.Parse(match.Groups[1].Value) * 1024 * 1024).Should().BeGreaterThanOrEqualTo(ApiMaxFileSizeBytes + MultipartAllowanceBytes);
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "docker-compose.yml")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
