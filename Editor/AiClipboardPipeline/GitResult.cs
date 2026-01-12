#if UNITY_EDITOR_WIN
using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace AiClipboardPipeline.Editor
{
    internal readonly struct GitResult
    {
        public int ExitCode { get; }
        public string StdOut { get; }
        public string StdErr { get; }

        public GitResult(int exitCode, string stdOut, string stdErr)
        {
            ExitCode = exitCode;
            StdOut = stdOut ?? string.Empty;
            StdErr = stdErr ?? string.Empty;
        }
    }

    internal sealed class GitService
    {
        private const int DefaultTimeoutMs = 20_000;

        public GitResult Run(string workingDirectory, string args, int timeoutMs = DefaultTimeoutMs)
        {
            ProcessStartInfo psi = new()
            {
                FileName = "git",
                Arguments = args,
                WorkingDirectory = workingDirectory,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using Process p = new() { StartInfo = psi };

            try
            {
                p.Start();

                // Read asynchronously to avoid deadlocks on large outputs.
                Task<string> outTask = p.StandardOutput.ReadToEndAsync();
                Task<string> errTask = p.StandardError.ReadToEndAsync();

                if (!p.WaitForExit(timeoutMs))
                {
                    try
                    {
                        p.Kill();
                    }
                    catch
                    {
                    }

                    return new GitResult(
                        -2,
                        string.Empty,
                        $"git timed out after {timeoutMs}ms.\nArgs: {args}");
                }

                string stdout = outTask.GetAwaiter().GetResult();
                string stderr = errTask.GetAwaiter().GetResult();

                return new GitResult(
                    p.ExitCode,
                    stdout,
                    stderr);
            }
            catch (Exception ex)
            {
                return new GitResult(
                    -1,
                    string.Empty,
                    "Failed to start git process.\n\n" + ex);
            }
        }
    }
}
#endif