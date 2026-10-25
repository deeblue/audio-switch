using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace AudioSwitch.Platform.Windows.Tests;

internal static class LiveStateSnapshotGuard
{
    private static int _stopRequested;
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string Python = Path.Combine(RepositoryRoot, "probes", ".venv", "Scripts", "python.exe");

    public static void Run(string testName, Action test) =>
        RunAsync(testName, () =>
        {
            test();
            return Task.CompletedTask;
        }).GetAwaiter().GetResult();

    public static async Task RunAsync(string testName, Func<Task> test)
    {
        if (Volatile.Read(ref _stopRequested) != 0)
        {
            throw new InvalidOperationException("A prior live test or state comparison failed; later state-writing live tests are blocked for this test process.");
        }

        var suffix = Guid.NewGuid().ToString("N");
        var beforeName = $"stage3-{testName}-{suffix}-before";
        var afterName = $"stage3-{testName}-{suffix}-after";
        try
        {
            await RunPythonAsync("probes/audio_topology.py", "snapshot", beforeName).ConfigureAwait(false);
        }
        catch
        {
            Interlocked.Exchange(ref _stopRequested, 1);
            throw;
        }

        Exception? testError = null;
        try
        {
            await test().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            testError = exception;
        }

        try
        {
            await RunPythonAsync("probes/audio_topology.py", "snapshot", afterName).ConfigureAwait(false);
            var comparison = await RunPythonAsync("probes/compare_state.py",
                Path.Combine("probes", "out", beforeName + ".json"),
                Path.Combine("probes", "out", afterName + ".json"), allowMismatch: true).ConfigureAwait(false);
            if (comparison.ExitCode != 0)
            {
                Interlocked.Exchange(ref _stopRequested, 1);
                throw new InvalidOperationException(
                    $"Live test {testName} changed endpoint state. Stop further live tests and inspect the physical state changes:\n{comparison.Output}", testError);
            }
        }
        catch (Exception stateError) when (testError is not null && !ReferenceEquals(stateError, testError))
        {
            Interlocked.Exchange(ref _stopRequested, 1);
            throw new AggregateException("Live test failed and the post-test state check also failed.", testError, stateError);
        }
        catch
        {
            Interlocked.Exchange(ref _stopRequested, 1);
            throw;
        }

        if (testError is not null)
        {
            Interlocked.Exchange(ref _stopRequested, 1);
            ExceptionDispatchInfo.Capture(testError).Throw();
        }
    }

    private static async Task<(int ExitCode, string Output)> RunPythonAsync(string script, string firstArgument,
        string? secondArgument = null, bool allowMismatch = false)
    {
        var start = new ProcessStartInfo(Python)
        {
            WorkingDirectory = RepositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(script);
        start.ArgumentList.Add(firstArgument);
        if (secondArgument is not null) start.ArgumentList.Add(secondArgument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the probe Python process.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"Python probe timed out: {script}");
        }
        var output = (await stdout.ConfigureAwait(false)) + (await stderr.ConfigureAwait(false));
        if (process.ExitCode != 0 && !allowMismatch)
        {
            throw new InvalidOperationException($"Python probe failed ({script}, exit {process.ExitCode}):\n{output}");
        }
        return (process.ExitCode, output);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "probes", "audio_topology.py"))) return directory.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate the repository root containing probes/audio_topology.py.");
    }
}
