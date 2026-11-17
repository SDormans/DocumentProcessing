using System.Diagnostics;

namespace DocumentProcessing.Infrastructure.Ocr;

internal static class ExternalProcess
{
    public static async Task<string> RunAsync(string fileName, IEnumerable<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        var stdout = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeoutSource.Token);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException($"{Path.GetFileName(fileName)} did not finish within {timeout}.");
        }

        if (process.ExitCode != 0)
        {
            var error = (await stderr).Trim();
            throw new InvalidOperationException(
                $"{Path.GetFileName(fileName)} exited with code {process.ExitCode}: {(error.Length > 300 ? error[..300] : error)}");
        }
        return await stdout;
    }

    public static bool IsOnPath(string fileName)
    {
        if (Path.IsPathRooted(fileName))
            return File.Exists(fileName);
        return (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(directory => File.Exists(Path.Combine(directory, fileName)));
    }
}
