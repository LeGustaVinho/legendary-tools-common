using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AiClipboardPipeline.Editor
{
    internal static class ManualUnifiedDiffApplier
    {
        private const int SearchRadiusLines = 200;
        private const int MaxCandidateStartsToTry = 30;

        private sealed class FilePatch
        {
            public string OldPath;
            public string NewPath;

            public bool IsNewFile;
            public bool IsDeleteFile;

            // "\ No newline at end of file" markers.
            public bool OldNoNewlineAtEnd;
            public bool NewNoNewlineAtEnd;

            public readonly List<Hunk> Hunks = new();
        }

        private sealed class Hunk
        {
            public int OldStart;
            public int OldCount;
            public int NewStart;
            public int NewCount;

            // Includes prefix char in [0] (one of ' ', '+', '-').
            public readonly List<string> Lines = new();

            public int ConsumedOldLineCount
            {
                get
                {
                    int c = 0;
                    for (int i = 0; i < Lines.Count; i++)
                    {
                        char k = Lines[i][0];
                        if (k == ' ' || k == '-')
                            c++;
                    }

                    return c;
                }
            }
        }

        private readonly struct AnchorCheck
        {
            public readonly int OldOffset;
            public readonly string Content;
            public readonly int Weight;

            public AnchorCheck(int oldOffset, string content, int weight)
            {
                OldOffset = oldOffset;
                Content = content ?? string.Empty;
                Weight = weight;
            }
        }

        private sealed class FileTextSnapshot
        {
            public Encoding Encoding;
            public bool HasBom;
            public string PreferredEol; // "\n" or "\r\n"
            public bool EndedWithNewline;

            // Normalized LF text for applying.
            public string TextLf;
            public List<string> LinesLf;
        }

        public static bool TryApply(string projectRoot, string unifiedDiffText, out string errorReport,
            out List<string> touchedFiles)
        {
            touchedFiles = new List<string>();

            try
            {
                if (string.IsNullOrWhiteSpace(unifiedDiffText))
                {
                    errorReport = "Manual apply failed: patch text is empty.";
                    return false;
                }

                List<FilePatch> patches = Parse(unifiedDiffText, out string parseError);
                if (patches == null)
                {
                    errorReport = "Manual apply failed: parse error.\n\n" + parseError;
                    return false;
                }

                foreach (FilePatch fp in patches)
                {
                    if (fp == null)
                        continue;

                    if (fp.IsNewFile && fp.IsDeleteFile)
                    {
                        errorReport =
                            "Manual apply failed: patch indicates both new file and delete file for the same entry.";
                        return false;
                    }

                    if (string.IsNullOrEmpty(fp.NewPath) && string.IsNullOrEmpty(fp.OldPath))
                    {
                        errorReport = "Manual apply failed: missing file paths.";
                        return false;
                    }

                    string rel = !string.IsNullOrEmpty(fp.NewPath) ? fp.NewPath : fp.OldPath;
                    string fullPath = CombinePath(projectRoot, rel);

                    if (!IsInsideDirectory(fullPath, projectRoot))
                    {
                        errorReport = "Manual apply blocked: file path escapes project root.\n\nPath: " + fullPath;
                        return false;
                    }

                    if (fp.IsDeleteFile)
                    {
                        if (File.Exists(fullPath))
                        {
                            File.Delete(fullPath);
                            touchedFiles.Add(rel);
                        }

                        continue;
                    }

                    if (fp.IsNewFile)
                    {
                        if (!TryBuildNewFileText(fp, out string newFileTextLf, out string newFileError))
                        {
                            errorReport = "Manual apply failed while creating file:\n" + rel + "\n\n" + newFileError;
                            return false;
                        }

                        // Default choices for a new file.
                        Encoding enc = new UTF8Encoding(false);
                        bool hasBom = false;
                        string eol = "\n";

                        string finalText = ConvertLfToEol(newFileTextLf, eol);

                        Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                        WriteAllTextPreserveEncoding(fullPath, finalText, enc, hasBom);
                        touchedFiles.Add(rel);
                        continue;
                    }

                    if (!File.Exists(fullPath))
                    {
                        errorReport = "Manual apply failed: target file does not exist.\n\nPath: " + rel;
                        return false;
                    }

                    FileTextSnapshot snapshot = ReadFileSnapshot(fullPath);

                    if (!TryApplyToExistingFile(snapshot.LinesLf, fp, out List<string> newLinesLf,
                            out string applyError))
                    {
                        errorReport = "Manual apply failed for file:\n" + rel + "\n\n" + applyError;
                        return false;
                    }

                    // Unified diff can only reliably specify EOF newline when it includes the marker.
                    // If no marker exists, preserve the current file's EOF newline behavior.
                    bool shouldEndWithNewline;
                    if (fp.OldNoNewlineAtEnd || fp.NewNoNewlineAtEnd)
                        shouldEndWithNewline = !fp.NewNoNewlineAtEnd; // new side decides
                    else
                        shouldEndWithNewline = snapshot.EndedWithNewline;

                    NormalizeTrailingNewline(newLinesLf, shouldEndWithNewline);

                    string resultTextLf = string.Join("\n", newLinesLf);
                    string resultText = ConvertLfToEol(resultTextLf, snapshot.PreferredEol);

                    WriteAllTextPreserveEncoding(fullPath, resultText, snapshot.Encoding, snapshot.HasBom);
                    touchedFiles.Add(rel);
                }

                errorReport = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                errorReport = "Manual apply failed with exception:\n\n" + ex;
                return false;
            }
        }

        private static bool TryApplyToExistingFile(List<string> originalLinesLf, FilePatch fp,
            out List<string> newLinesLf, out string error)
        {
            newLinesLf = new List<string>(originalLinesLf.Count);
            error = string.Empty;

            int currentOldIndex = 0;

            foreach (Hunk hunk in fp.Hunks)
            {
                int nominal = Math.Max(currentOldIndex, Math.Max(0, hunk.OldStart - 1));

                int windowStart = Math.Max(currentOldIndex, nominal - SearchRadiusLines);
                int windowEndExclusive = Math.Min(originalLinesLf.Count + 1, nominal + SearchRadiusLines + 1);

                List<int> candidates =
                    BuildCandidateStarts(originalLinesLf, hunk, windowStart, windowEndExclusive, nominal);
                if (candidates.Count == 0)
                {
                    error =
                        "Failed to locate hunk start (no candidates).\n" +
                        $"Hunk: @@ -{hunk.OldStart},{hunk.OldCount} +{hunk.NewStart},{hunk.NewCount} @@";
                    return false;
                }

                bool applied = false;
                string lastTryError = string.Empty;

                for (int i = 0; i < candidates.Count && i < MaxCandidateStartsToTry; i++)
                {
                    int startIndex = candidates[i];
                    if (startIndex < currentOldIndex)
                        continue;

                    if (!TryApplyHunkAt(originalLinesLf, hunk, startIndex, out int consumedOld,
                            out List<string> producedNew, out string hunkError))
                    {
                        lastTryError = hunkError;
                        continue;
                    }

                    // Copy unchanged lines up to the chosen start.
                    while (currentOldIndex < startIndex && currentOldIndex < originalLinesLf.Count)
                    {
                        newLinesLf.Add(originalLinesLf[currentOldIndex]);
                        currentOldIndex++;
                    }

                    // Apply hunk output.
                    newLinesLf.AddRange(producedNew);
                    currentOldIndex = startIndex + consumedOld;

                    applied = true;
                    break;
                }

                if (!applied)
                {
                    error =
                        "Failed to apply hunk (strict match) within search window.\n\n" +
                        $"Hunk: @@ -{hunk.OldStart},{hunk.OldCount} +{hunk.NewStart},{hunk.NewCount} @@\n\n" +
                        "Last error:\n" + lastTryError;
                    return false;
                }
            }

            // Copy remaining lines.
            while (currentOldIndex < originalLinesLf.Count)
            {
                newLinesLf.Add(originalLinesLf[currentOldIndex]);
                currentOldIndex++;
            }

            return true;
        }

        private static bool TryApplyHunkAt(
            List<string> originalLinesLf,
            Hunk hunk,
            int startIndex,
            out int consumedOldLines,
            out List<string> producedNewLines,
            out string error)
        {
            consumedOldLines = 0;
            producedNewLines = new List<string>(hunk.Lines.Count);
            error = string.Empty;

            int idx = startIndex;

            if (startIndex < 0 || startIndex > originalLinesLf.Count)
            {
                error = "Invalid hunk start index.";
                return false;
            }

            for (int i = 0; i < hunk.Lines.Count; i++)
            {
                string pline = hunk.Lines[i];
                if (string.IsNullOrEmpty(pline))
                {
                    error = "Encountered empty patch line in hunk.";
                    return false;
                }

                char kind = pline[0];
                string content = pline.Length > 1 ? pline.Substring(1) : string.Empty;

                switch (kind)
                {
                    case ' ':
                    {
                        if (idx >= originalLinesLf.Count)
                        {
                            error = "Context line expected but reached end of file.\n" +
                                    $"Expected: \"{content}\"";
                            return false;
                        }

                        string actual = originalLinesLf[idx];
                        if (!StringEqualsExact(actual, content))
                        {
                            error =
                                "Context mismatch.\n" +
                                $"At line {idx + 1}\n" +
                                $"Expected: \"{content}\"\n" +
                                $"Actual:   \"{actual}\"";
                            return false;
                        }

                        producedNewLines.Add(actual);
                        idx++;
                        break;
                    }

                    case '-':
                    {
                        if (idx >= originalLinesLf.Count)
                        {
                            error = "Delete line expected but reached end of file.\n" +
                                    $"Expected to delete: \"{content}\"";
                            return false;
                        }

                        string actual = originalLinesLf[idx];
                        if (!StringEqualsExact(actual, content))
                        {
                            error =
                                "Delete mismatch.\n" +
                                $"At line {idx + 1}\n" +
                                $"Expected to delete: \"{content}\"\n" +
                                $"Actual:            \"{actual}\"";
                            return false;
                        }

                        idx++;
                        break;
                    }

                    case '+':
                    {
                        producedNewLines.Add(content);
                        break;
                    }

                    default:
                        error = "Unknown patch line prefix: '" + kind + "'";
                        return false;
                }
            }

            consumedOldLines = idx - startIndex;

            int oldConsumed = hunk.ConsumedOldLineCount;
            int newProduced = CountProducedNewLines(hunk);

            if (oldConsumed != hunk.OldCount || newProduced != hunk.NewCount)
                if (Math.Abs(oldConsumed - hunk.OldCount) > 5 || Math.Abs(newProduced - hunk.NewCount) > 5)
                {
                    error =
                        "Hunk count validation failed.\n" +
                        $"Parsed oldConsumed={oldConsumed}, header oldCount={hunk.OldCount}\n" +
                        $"Parsed newProduced={newProduced}, header newCount={hunk.NewCount}";
                    return false;
                }

            return true;
        }

        private static int CountProducedNewLines(Hunk hunk)
        {
            int c = 0;
            for (int i = 0; i < hunk.Lines.Count; i++)
            {
                char k = hunk.Lines[i][0];
                if (k == ' ' || k == '+')
                    c++;
            }

            return c;
        }

        private static List<int> BuildCandidateStarts(
            List<string> originalLinesLf,
            Hunk hunk,
            int windowStart,
            int windowEndExclusive,
            int nominal)
        {
            List<AnchorCheck> anchors = BuildAnchorChecks(hunk, out AnchorCheck? primaryAnchor);

            HashSet<int> set = new();
            set.Add(nominal);

            if (primaryAnchor.HasValue)
            {
                AnchorCheck pa = primaryAnchor.Value;
                int searchFrom = Math.Max(0, windowStart);
                int searchTo = Math.Min(originalLinesLf.Count, windowEndExclusive);

                for (int i = searchFrom; i < searchTo; i++)
                {
                    if (!StringEqualsExact(originalLinesLf[i], pa.Content))
                        continue;

                    int candidate = i - pa.OldOffset;
                    if (candidate < windowStart || candidate >= windowEndExclusive)
                        continue;

                    if (candidate < 0 || candidate > originalLinesLf.Count)
                        continue;

                    set.Add(candidate);
                }
            }

            if (set.Count < 5)
            {
                int left = nominal - 1;
                int right = nominal + 1;

                while (set.Count < MaxCandidateStartsToTry && (left >= windowStart || right < windowEndExclusive))
                {
                    if (right < windowEndExclusive)
                        set.Add(right);

                    if (left >= windowStart)
                        set.Add(left);

                    right++;
                    left--;
                }
            }

            List<(int start, int score)> scored = new(set.Count);
            foreach (int c in set)
            {
                int maxConsume = hunk.ConsumedOldLineCount;
                if (c < 0 || c > originalLinesLf.Count)
                    continue;

                if (c + maxConsume > originalLinesLf.Count)
                    continue;

                int score = ScoreCandidate(originalLinesLf, c, anchors);
                scored.Add((c, score));
            }

            scored.Sort((a, b) =>
            {
                int cmp = b.score.CompareTo(a.score);
                if (cmp != 0)
                    return cmp;

                int da = Math.Abs(a.start - nominal);
                int db = Math.Abs(b.start - nominal);
                return da.CompareTo(db);
            });

            List<int> result = new(scored.Count);
            for (int i = 0; i < scored.Count; i++)
            {
                result.Add(scored[i].start);
            }

            return result;
        }

        private static int ScoreCandidate(List<string> originalLinesLf, int candidateStart, List<AnchorCheck> anchors)
        {
            int score = 0;

            for (int i = 0; i < anchors.Count; i++)
            {
                AnchorCheck a = anchors[i];
                int idx = candidateStart + a.OldOffset;
                if (idx < 0 || idx >= originalLinesLf.Count)
                {
                    score -= a.Weight;
                    continue;
                }

                if (StringEqualsExact(originalLinesLf[idx], a.Content))
                    score += a.Weight;
                else
                    score -= a.Weight;
            }

            return score;
        }

        private static List<AnchorCheck> BuildAnchorChecks(Hunk hunk, out AnchorCheck? primaryAnchor)
        {
            primaryAnchor = null;

            List<(int offset, string content)> contextOffsets = new();
            List<(int offset, string content)> removalOffsets = new();

            int oldOffset = 0;
            for (int i = 0; i < hunk.Lines.Count; i++)
            {
                string pline = hunk.Lines[i];
                if (string.IsNullOrEmpty(pline))
                    continue;

                char kind = pline[0];
                string content = pline.Length > 1 ? pline.Substring(1) : string.Empty;

                if (kind == ' ')
                    contextOffsets.Add((oldOffset, content));

                if (kind == '-')
                    removalOffsets.Add((oldOffset, content));

                if (kind == ' ' || kind == '-')
                    oldOffset++;
            }

            List<AnchorCheck> anchors = new();

            if (contextOffsets.Count > 0)
            {
                (int offset, string content) first = contextOffsets[0];
                (int offset, string content) last = contextOffsets[contextOffsets.Count - 1];

                anchors.Add(new AnchorCheck(first.offset, first.content, 8));
                anchors.Add(new AnchorCheck(last.offset, last.content, 8));

                primaryAnchor = new AnchorCheck(first.offset, first.content, 8);

                for (int i = 0; i + 1 < contextOffsets.Count; i++)
                {
                    (int offset, string content) a = contextOffsets[i];
                    (int offset, string content) b = contextOffsets[i + 1];

                    if (b.offset == a.offset + 1)
                    {
                        anchors.Add(new AnchorCheck(a.offset, a.content, 6));
                        anchors.Add(new AnchorCheck(b.offset, b.content, 6));
                        break;
                    }
                }

                for (int i = 1; i < Math.Min(3, contextOffsets.Count); i++)
                {
                    anchors.Add(new AnchorCheck(contextOffsets[i].offset, contextOffsets[i].content, 2));
                }

                for (int i = Math.Max(0, contextOffsets.Count - 3); i < contextOffsets.Count - 1; i++)
                {
                    anchors.Add(new AnchorCheck(contextOffsets[i].offset, contextOffsets[i].content, 2));
                }
            }
            else if (removalOffsets.Count > 0)
            {
                (int offset, string content) first = removalOffsets[0];
                anchors.Add(new AnchorCheck(first.offset, first.content, 6));
                primaryAnchor = new AnchorCheck(first.offset, first.content, 6);

                for (int i = 1; i < Math.Min(3, removalOffsets.Count); i++)
                {
                    anchors.Add(new AnchorCheck(removalOffsets[i].offset, removalOffsets[i].content, 4));
                }
            }

            for (int i = 0; i < Math.Min(2, removalOffsets.Count); i++)
            {
                anchors.Add(new AnchorCheck(removalOffsets[i].offset, removalOffsets[i].content, 5));
            }

            anchors = anchors
                .GroupBy(a => a.OldOffset.ToString() + "\u0001" + a.Content)
                .Select(g => g.OrderByDescending(x => x.Weight).First())
                .ToList();

            return anchors;
        }

        private static bool TryBuildNewFileText(FilePatch fp, out string newFileTextLf, out string error)
        {
            error = string.Empty;
            List<string> resultLines = new();

            foreach (Hunk hunk in fp.Hunks)
            {
                for (int i = 0; i < hunk.Lines.Count; i++)
                {
                    string pline = hunk.Lines[i];
                    if (string.IsNullOrEmpty(pline))
                        continue;

                    char kind = pline[0];
                    string content = pline.Length > 1 ? pline.Substring(1) : string.Empty;

                    if (kind == '+')
                    {
                        resultLines.Add(content);
                        continue;
                    }

                    if (kind == ' ')
                    {
                        resultLines.Add(content);
                        continue;
                    }

                    if (kind == '-')
                    {
                        error = "New file patch contains deletions, which is not supported safely.";
                        newFileTextLf = string.Empty;
                        return false;
                    }

                    error = "Unknown patch line prefix in new file: '" + kind + "'";
                    newFileTextLf = string.Empty;
                    return false;
                }
            }

            bool shouldEndWithNewline = !fp.NewNoNewlineAtEnd;
            NormalizeTrailingNewline(resultLines, shouldEndWithNewline);

            newFileTextLf = string.Join("\n", resultLines);
            return true;
        }

        private static void NormalizeTrailingNewline(List<string> linesLf, bool shouldEndWithNewline)
        {
            if (shouldEndWithNewline)
            {
                if (linesLf.Count == 0)
                {
                    linesLf.Add(string.Empty);
                    return;
                }

                if (linesLf[linesLf.Count - 1] != string.Empty)
                    linesLf.Add(string.Empty);
            }
            else
            {
                if (linesLf.Count > 0 && linesLf[linesLf.Count - 1] == string.Empty)
                    linesLf.RemoveAt(linesLf.Count - 1);
            }
        }

        private static string ConvertLfToEol(string textLf, string preferredEol)
        {
            if (string.IsNullOrEmpty(textLf))
                return textLf ?? string.Empty;

            if (string.Equals(preferredEol, "\r\n", StringComparison.Ordinal))
                return textLf.Replace("\n", "\r\n");

            return textLf;
        }

        private static FileTextSnapshot ReadFileSnapshot(string fullPath)
        {
            byte[] bytes = File.ReadAllBytes(fullPath);

            DetectEncoding(bytes, out Encoding enc, out bool hasBom, out int bomLen);

            string text = enc.GetString(bytes, bomLen, bytes.Length - bomLen);

            string preferredEol = DetectPreferredEol(text);
            bool endedWithNewline = EndsWithAnyNewline(text);

            string textLf = NormalizeToLF(text);
            List<string> linesLf = textLf.Split('\n').ToList();

            return new FileTextSnapshot
            {
                Encoding = enc,
                HasBom = hasBom,
                PreferredEol = preferredEol,
                EndedWithNewline = endedWithNewline,
                TextLf = textLf,
                LinesLf = linesLf
            };
        }

        private static void WriteAllTextPreserveEncoding(string fullPath, string text, Encoding enc, bool hasBom)
        {
            Encoding writeEnc = GetWriteEncoding(enc, hasBom);

            using (FileStream fs = new(fullPath, FileMode.Create, FileAccess.Write, FileShare.Read))
            using (StreamWriter sw = new(fs, writeEnc))
            {
                sw.Write(text ?? string.Empty);
            }
        }

        private static Encoding GetWriteEncoding(Encoding enc, bool hasBom)
        {
            if (enc is UTF8Encoding)
                return new UTF8Encoding(hasBom);

            if (enc.CodePage == Encoding.Unicode.CodePage)
                return new UnicodeEncoding(false, hasBom);

            if (enc.CodePage == Encoding.BigEndianUnicode.CodePage)
                return new UnicodeEncoding(true, hasBom);

            return enc;
        }

        private static void DetectEncoding(byte[] bytes, out Encoding enc, out bool hasBom, out int bomLength)
        {
            enc = new UTF8Encoding(false);
            hasBom = false;
            bomLength = 0;

            if (bytes == null || bytes.Length < 2)
                return;

            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                enc = new UTF8Encoding(true);
                hasBom = true;
                bomLength = 3;
                return;
            }

            if (bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                enc = Encoding.Unicode;
                hasBom = true;
                bomLength = 2;
                return;
            }

            if (bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                enc = Encoding.BigEndianUnicode;
                hasBom = true;
                bomLength = 2;
                return;
            }

            enc = new UTF8Encoding(false);
            hasBom = false;
            bomLength = 0;
        }

        private static string DetectPreferredEol(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "\n";

            int crlf = 0;
            int lf = 0;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\n')
                {
                    if (i > 0 && text[i - 1] == '\r')
                        crlf++;
                    else
                        lf++;
                }
            }

            if (crlf > lf)
                return "\r\n";

            return "\n";
        }

        private static bool EndsWithAnyNewline(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            return text.EndsWith("\n", StringComparison.Ordinal) ||
                   text.EndsWith("\r", StringComparison.Ordinal);
        }

        private static List<FilePatch> Parse(string text, out string error)
        {
            error = string.Empty;

            string t = NormalizeToLF(text);
            string[] lines = t.Split('\n');

            List<FilePatch> patches = new();

            FilePatch current = null;
            Hunk currentHunk = null;

            char lastNonMarkerPrefix = '\0';

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                if (line.StartsWith("diff --git ", StringComparison.Ordinal))
                {
                    current = new FilePatch();
                    currentHunk = null;
                    patches.Add(current);
                    lastNonMarkerPrefix = '\0';
                    continue;
                }

                if (line.StartsWith("rename from ", StringComparison.Ordinal) ||
                    line.StartsWith("rename to ", StringComparison.Ordinal) ||
                    line.StartsWith("copy from ", StringComparison.Ordinal) ||
                    line.StartsWith("copy to ", StringComparison.Ordinal) ||
                    line.StartsWith("GIT binary patch", StringComparison.Ordinal))
                {
                    error = "Unsupported diff operation detected (rename/copy/binary patch).";
                    return null;
                }

                if (current == null)
                {
                    if (line.StartsWith("--- ", StringComparison.Ordinal))
                    {
                        current = new FilePatch();
                        patches.Add(current);
                    }
                    else
                    {
                        continue;
                    }
                }

                if (line.StartsWith("--- ", StringComparison.Ordinal))
                {
                    string p = line.Substring(4).Trim();
                    current.OldPath = ParsePathToken(p, out bool isDevNullOld);
                    if (isDevNullOld)
                        current.IsNewFile = true;

                    currentHunk = null;
                    lastNonMarkerPrefix = '\0';
                    continue;
                }

                if (line.StartsWith("+++ ", StringComparison.Ordinal))
                {
                    string p = line.Substring(4).Trim();
                    current.NewPath = ParsePathToken(p, out bool isDevNullNew);
                    if (isDevNullNew)
                        current.IsDeleteFile = true;

                    continue;
                }

                if (line.StartsWith("@@ ", StringComparison.Ordinal))
                {
                    currentHunk = ParseHunkHeader(line, out string hunkErr);
                    if (currentHunk == null)
                    {
                        error = "Failed to parse hunk header:\n" + hunkErr + "\n\nLine: " + line;
                        return null;
                    }

                    current.Hunks.Add(currentHunk);
                    lastNonMarkerPrefix = '\0';
                    continue;
                }

                if (line.StartsWith("\\ No newline at end of file", StringComparison.Ordinal))
                {
                    if (lastNonMarkerPrefix == '-')
                    {
                        current.OldNoNewlineAtEnd = true;
                    }
                    else if (lastNonMarkerPrefix == '+')
                    {
                        current.NewNoNewlineAtEnd = true;
                    }
                    else if (lastNonMarkerPrefix == ' ')
                    {
                        current.OldNoNewlineAtEnd = true;
                        current.NewNoNewlineAtEnd = true;
                    }

                    continue;
                }

                if (currentHunk != null)
                {
                    if (string.IsNullOrEmpty(line))
                        continue;

                    char prefix = line[0];
                    if (prefix == ' ' || prefix == '+' || prefix == '-')
                    {
                        currentHunk.Lines.Add(line);
                        lastNonMarkerPrefix = prefix;
                    }
                }
            }

            foreach (FilePatch fp in patches)
            {
                if (fp.IsDeleteFile && fp.Hunks.Count == 0)
                    continue;

                if (!fp.IsDeleteFile && fp.Hunks.Count == 0)
                {
                    error = "No hunks found for one of the file entries.";
                    return null;
                }
            }

            return patches;
        }

        private static Hunk ParseHunkHeader(string line, out string error)
        {
            error = string.Empty;

            int firstAt = line.IndexOf("@@", StringComparison.Ordinal);
            int secondAt = line.IndexOf("@@", firstAt + 2, StringComparison.Ordinal);
            if (firstAt != 0 || secondAt < 0)
            {
                error = "Invalid @@ header format.";
                return null;
            }

            string inner = line.Substring(2, secondAt - 2).Trim();
            string[] parts = inner.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                error = "Invalid @@ header tokens.";
                return null;
            }

            if (!parts[0].StartsWith("-", StringComparison.Ordinal) ||
                !parts[1].StartsWith("+", StringComparison.Ordinal))
            {
                error = "Missing -old/+new in @@ header.";
                return null;
            }

            if (!TryParseRange(parts[0].Substring(1), out int oStart, out int oCount) ||
                !TryParseRange(parts[1].Substring(1), out int nStart, out int nCount))
            {
                error = "Failed to parse hunk ranges.";
                return null;
            }

            return new Hunk
            {
                OldStart = oStart,
                OldCount = oCount,
                NewStart = nStart,
                NewCount = nCount
            };
        }

        private static bool TryParseRange(string token, out int start, out int count)
        {
            start = 0;
            count = 0;

            int comma = token.IndexOf(',', StringComparison.Ordinal);
            if (comma < 0)
            {
                if (!int.TryParse(token, out start))
                    return false;

                count = 1;
                return true;
            }

            string a = token.Substring(0, comma);
            string b = token.Substring(comma + 1);

            if (!int.TryParse(a, out start))
                return false;

            if (!int.TryParse(b, out count))
                return false;

            return true;
        }

        private static string ParsePathToken(string token, out bool isDevNull)
        {
            isDevNull = false;

            if (string.Equals(token, "/dev/null", StringComparison.Ordinal))
            {
                isDevNull = true;
                return string.Empty;
            }

            if (token.Length >= 2 && (token[0] == 'a' || token[0] == 'b') && token[1] == '/')
                token = token.Substring(2);

            return token.Trim();
        }

        private static string CombinePath(string root, string rel)
        {
            string[] parts = rel.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Aggregate(root, Path.Combine);
        }

        private static bool IsInsideDirectory(string fullPath, string directory)
        {
            string full = Path.GetFullPath(fullPath);
            string dir = Path.GetFullPath(directory);

            if (!dir.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                dir += Path.DirectorySeparatorChar;

            return full.StartsWith(dir, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeToLF(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            return text.Replace("\r\n", "\n").Replace("\r", "\n");
        }

        private static bool StringEqualsExact(string a, string b)
        {
            return string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.Ordinal);
        }
    }
}