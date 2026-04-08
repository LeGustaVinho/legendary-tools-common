namespace LargeWorldCoordinates
{
    /// <summary>
    /// Defines the official scale relationship between Unity units and global units.
    /// </summary>
    public static class WorldScale
    {
        /// <summary>
        /// Gets the number of global units per Unity unit.
        /// One Unity unit is one meter, and one global unit is one centimeter.
        /// Therefore, one Unity unit equals 100 global units.
        /// </summary>
        public const double GlobalUnitsPerUnityUnit = 100.0d;
    }
}
