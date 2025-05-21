using System;

namespace LegendaryTools.AttributeSystem.HexGrid
{
    public struct FractionalHex
    {
        public readonly float Q;
        public readonly float R;
        public readonly float S;

        public FractionalHex(float q, float r, float s)
        {
            Q = q;
            R = r;
            S = s;
            if (Math.Round(q + r + s) != 0)
            {
                throw new ArgumentException("q + r + s must be 0");
            }
        }

        public Hex Round()
        {
            int qi = (int) Math.Round(Q);
            int ri = (int) Math.Round(R);
            int si = (int) Math.Round(S);
            float q_diff = Math.Abs(qi - Q);
            float r_diff = Math.Abs(ri - R);
            float s_diff = Math.Abs(si - S);
            if (q_diff > r_diff && q_diff > s_diff)
            {
                qi = -ri - si;
            }
            else if (r_diff > s_diff)
            {
                ri = -qi - si;
            }
            else
            {
                si = -qi - ri;
            }

            return new Hex(qi, ri, si);
        }

        public FractionalHex Lerp(FractionalHex b, float t)
        {
            return new FractionalHex(Q * (1.0f - t) + b.Q * t, R * (1.0f - t) + b.R * t, S * (1.0f - t) + b.S * t);
        }

        public static Hex[] Line(Hex a, Hex b)
        {
            int N = a.Distance(b);
            Hex[] results = new Hex[N + 1];

            FractionalHex a_nudge = new FractionalHex(a.Q + 0.000001f, a.R + 0.000001f, a.S - 0.000002f);
            FractionalHex b_nudge = new FractionalHex(b.Q + 0.000001f, b.R + 0.000001f, b.S - 0.000002f);

            float step = 1.0f / Math.Max(N, 1);
            for (int i = 0; i <= N; i++)
            {
                results[i] = a_nudge.Lerp(b_nudge, step * i).Round();
            }

            return results;
        }
    }
}