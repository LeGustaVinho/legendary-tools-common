namespace LegendaryTools.Tests.ECS.Boids
{
    /// <summary>
    /// Fixed-point boid position (Scale = 1000).
    /// </summary>
    public struct BoidPosition
    {
        public int X;
        public int Y;

        public BoidPosition(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    /// <summary>
    /// Fixed-point boid velocity (Scale = 1000).
    /// </summary>
    public struct BoidVelocity
    {
        public int X;
        public int Y;

        public BoidVelocity(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    /// <summary>
    /// Next-state position buffer (Scale = 1000).
    /// </summary>
    public struct BoidNextPosition
    {
        public int X;
        public int Y;

        public BoidNextPosition(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    /// <summary>
    /// Next-state velocity buffer (Scale = 1000).
    /// </summary>
    public struct BoidNextVelocity
    {
        public int X;
        public int Y;

        public BoidNextVelocity(int x, int y)
        {
            X = x;
            Y = y;
        }
    }
}