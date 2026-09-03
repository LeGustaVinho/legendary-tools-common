using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace LegendaryTools.Editor.DomainReload
{
    /// <summary>
    /// Parses Unity's Editor.log timing blocks. The format is text-only and not a public API, so the
    /// parser is deliberately tolerant of indentation, decimal separators and newly-added steps.
    /// </summary>
    public static class DomainReloadLogParser
    {
        private static readonly Regex DomainHeader = new(
            @"^Domain Reload Profiling:\s*(?<ms>[\d.,]+)ms\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex TimingLine = new(
            @"^(?<indent>[\t ]*)(?<name>.+?)\s*\((?<ms>[\d.,]+)ms\)\s*$",
            RegexOptions.Compiled);

        private static readonly Regex AssetHeader = new(
            @"^Asset Pipeline Refresh \(id=(?<id>[^)]+)\): Total:\s*(?<seconds>[\d.,]+) seconds - Initiated by (?<initiator>.+)$",
            RegexOptions.Compiled);

        private static readonly Regex ScriptingSummary = new(
            @"Scripting:\s*domain reloads=(?<count>\d+), domain reload time=(?<reload>[\d.,]+) ms, compile time=(?<compile>[\d.,]+) ms",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex ProcessSummary = new(
            @"Asset DB Process Time:\s*managed=(?<managed>[\d.,]+) ms, native=(?<native>[\d.,]+) ms",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex AssetDetailTiming = new(
            @"^[\t ]*(?<name>[^:]+):\s*(?<ms>[\d.,]+)ms(?:\s|$)",
            RegexOptions.Compiled);

        public static List<DomainReloadReport> Parse(string text, string logPath = null, long logOffset = 0)
        {
            List<DomainReloadReport> reports = new();
            if (string.IsNullOrEmpty(text))
                return reports;

            string[] lines = Normalize(text).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                Match header = DomainHeader.Match(lines[i].TrimEnd());
                if (!header.Success)
                    continue;

                DomainReloadReport report = new()
                {
                    TotalMs = ParseNumber(header.Groups["ms"].Value),
                    Completed = true,
                    Status = "Completed",
                    LogPath = logPath,
                    LogOffset = logOffset
                };

                List<DomainReloadStep> flat = new();
                int cursor = i + 1;
                for (; cursor < lines.Length; cursor++)
                {
                    Match timing = TimingLine.Match(lines[cursor].TrimEnd());
                    if (!timing.Success)
                        break;

                    int depth = GetIndentDepth(timing.Groups["indent"].Value);
                    flat.Add(new DomainReloadStep
                    {
                        Name = timing.Groups["name"].Value.Trim(),
                        DurationMs = ParseNumber(timing.Groups["ms"].Value),
                        Depth = depth
                    });
                }

                report.Steps = BuildTree(flat);
                CalculateSelfTimes(report.Steps);
                report.Evidence = ExtractEvidence(lines, FindReloadStart(lines, i), cursor);
                report.AssetPipeline = ParseNearestAssetPipeline(lines, cursor);
                reports.Add(report);
                i = Math.Max(i, cursor - 1);
            }

            return reports;
        }

        public static DomainReloadReport ParseIncomplete(string text, string logPath = null, long logOffset = 0)
        {
            if (string.IsNullOrEmpty(text))
                return null;

            string[] lines = Normalize(text).Split('\n');
            int start = -1;
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                if (lines[i].IndexOf("Begin MonoManager ReloadAssembly", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    start = i;
                    break;
                }
            }

            if (start < 0)
                return null;

            bool hasCompletedReport = lines.Skip(start).Any(line => DomainHeader.IsMatch(line.TrimEnd()));
            if (hasCompletedReport)
                return null;

            List<string> tail = lines.Skip(start).Where(line => !string.IsNullOrWhiteSpace(line)).TakeLast(120).ToList();
            return new DomainReloadReport
            {
                Completed = false,
                Status = InferIncompleteStage(tail),
                LogPath = logPath,
                LogOffset = logOffset,
                Evidence = tail
            };
        }

        private static AssetPipelineTiming ParseNearestAssetPipeline(string[] lines, int start)
        {
            int max = Math.Min(lines.Length, start + 180);
            for (int i = start; i < max; i++)
            {
                Match header = AssetHeader.Match(lines[i].Trim());
                if (!header.Success)
                    continue;

                AssetPipelineTiming timing = new()
                {
                    Id = header.Groups["id"].Value,
                    Initiator = header.Groups["initiator"].Value.Trim(),
                    TotalMs = ParseNumber(header.Groups["seconds"].Value) * 1000.0
                };

                int detailDepth = 0;
                for (int j = i + 1; j < Math.Min(lines.Length, i + 100); j++)
                {
                    string line = lines[j];
                    if (j > i + 1 && AssetHeader.IsMatch(line.Trim()))
                        break;

                    Match scripting = ScriptingSummary.Match(line);
                    if (scripting.Success)
                    {
                        timing.DomainReloadCount = (int)ParseNumber(scripting.Groups["count"].Value);
                        timing.DomainReloadMs = ParseNumber(scripting.Groups["reload"].Value);
                        timing.CompileMs = ParseNumber(scripting.Groups["compile"].Value);
                    }

                    Match process = ProcessSummary.Match(line);
                    if (process.Success)
                    {
                        timing.ManagedProcessMs = ParseNumber(process.Groups["managed"].Value);
                        timing.NativeProcessMs = ParseNumber(process.Groups["native"].Value);
                    }

                    Match detail = AssetDetailTiming.Match(line);
                    if (detail.Success && !line.Contains("Summary:"))
                    {
                        timing.Steps.Add(new DomainReloadStep
                        {
                            Name = detail.Groups["name"].Value.Trim(),
                            DurationMs = ParseNumber(detail.Groups["ms"].Value),
                            Depth = detailDepth
                        });
                    }
                }

                return timing;
            }

            return null;
        }

        private static List<DomainReloadStep> BuildTree(List<DomainReloadStep> flat)
        {
            List<DomainReloadStep> roots = new();
            List<DomainReloadStep> stack = new();
            foreach (DomainReloadStep step in flat)
            {
                while (stack.Count > 0 && stack[^1].Depth >= step.Depth)
                    stack.RemoveAt(stack.Count - 1);

                if (stack.Count == 0)
                    roots.Add(step);
                else
                    stack[^1].Children.Add(step);

                stack.Add(step);
            }

            return roots;
        }

        private static void CalculateSelfTimes(IEnumerable<DomainReloadStep> steps)
        {
            foreach (DomainReloadStep step in steps)
            {
                CalculateSelfTimes(step.Children);
                double children = step.Children.Sum(child => child.DurationMs);
                step.SelfMs = Math.Max(0.0, step.DurationMs - children);
            }
        }

        private static int GetIndentDepth(string indent)
        {
            int columns = 0;
            foreach (char c in indent)
                columns += c == '\t' ? 4 : 1;
            return Math.Max(0, columns / 4 - 1);
        }

        private static int FindReloadStart(string[] lines, int headerIndex)
        {
            for (int i = headerIndex; i >= Math.Max(0, headerIndex - 300); i--)
                if (lines[i].IndexOf("Begin MonoManager ReloadAssembly", StringComparison.OrdinalIgnoreCase) >= 0)
                    return i;
            return Math.Max(0, headerIndex - 20);
        }

        private static List<string> ExtractEvidence(string[] lines, int start, int end)
        {
            string[] signals =
            {
                "exception", "error", "warning", "stackwalker", "duplicate assembly", "timed out",
                "deadlock", "cannot add menu item", "failed", "didreloaddomain", "initializeonload"
            };

            List<string> evidence = new();
            for (int i = Math.Max(0, start); i < Math.Min(lines.Length, end); i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line))
                    continue;
                if (signals.Any(signal => line.IndexOf(signal, StringComparison.OrdinalIgnoreCase) >= 0))
                    evidence.Add(line.Length > 700 ? line.Substring(0, 700) + "..." : line);
            }

            return evidence.Distinct().Take(100).ToList();
        }

        private static string InferIncompleteStage(IReadOnlyList<string> tail)
        {
            string joined = string.Join("\n", tail);
            if (joined.IndexOf("Finished resetting the current domain", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Reload finished, but the profiling block was not written";
            if (joined.IndexOf("Loaded All Assemblies", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Stalled after loading assemblies (likely initialization/restoration)";
            if (joined.IndexOf("Begin MonoManager ReloadAssembly", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Stalled while unloading the domain or loading assemblies";
            return "Incomplete capture";
        }

        private static double ParseNumber(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return 0;

            string normalized = value.Trim().Replace(',', '.');
            return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
                ? parsed
                : 0;
        }

        private static string Normalize(string text)
        {
            return text.Replace("\0", string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        }
    }
}
