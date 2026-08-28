using System.Diagnostics;

namespace GovernedAccess.Web.Evaluation;

internal static class EvaluationSourceCommitResolver
{
    internal static async Task<string> ResolveAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = Path.GetFullPath(workingDirectory),
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("rev-parse");
        startInfo.ArgumentList.Add("--verify");
        startInfo.ArgumentList.Add("HEAD");

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw InvalidSourceCommit();
        }

        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(
            cancellationToken);
        Task<string> standardError = process.StandardError.ReadToEndAsync(
            cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(standardOutput, standardError);
        }
        catch
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw;
        }

        string commit = standardOutput.Result.Trim();
        if (process.ExitCode != 0 || !IsCommitIdentifier(commit))
        {
            throw InvalidSourceCommit();
        }

        return commit.ToLowerInvariant();
    }

    private static bool IsCommitIdentifier(string value) =>
        value.Length is 40 or 64 && value.All(char.IsAsciiHexDigit);

    private static InvalidOperationException InvalidSourceCommit() =>
        new("The live-model evaluation source commit could not be resolved.");
}
