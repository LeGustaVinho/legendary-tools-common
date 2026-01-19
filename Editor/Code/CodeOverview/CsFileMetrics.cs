using System;

public readonly struct CsFileMetrics
{
    public int LineCount { get; }
    public int CoverageNumerator { get; }
    public int CoverageDenominator { get; }

    public CsFileMetrics(int lineCount, int coverageNumerator, int coverageDenominator)
    {
        LineCount = lineCount;

        CoverageDenominator = Math.Max(0, coverageDenominator);
        CoverageNumerator = Clamp(coverageNumerator, 0, CoverageDenominator);
    }

    public double Coverage01
    {
        get
        {
            if (CoverageDenominator <= 0) return 0.0;

            return (double)CoverageNumerator / CoverageDenominator;
        }
    }

    public int CoveragePercent
    {
        get
        {
            if (CoverageDenominator <= 0) return 0;

            return (int)Math.Round(Coverage01 * 100.0);
        }
    }

    public static CsFileMetrics Combine(CsFileMetrics a, CsFileMetrics b)
    {
        int linesA = a.LineCount < 0 ? 0 : a.LineCount;
        int linesB = b.LineCount < 0 ? 0 : b.LineCount;

        return new CsFileMetrics(
            linesA + linesB,
            a.CoverageNumerator + b.CoverageNumerator,
            a.CoverageDenominator + b.CoverageDenominator);
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min) return min;

        if (value > max) return max;

        return value;
    }
}