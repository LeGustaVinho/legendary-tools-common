using LegendaryTools.Common.Core.Patterns.ECS.Api;
using LegendaryTools.Common.Core.Patterns.ECS.Queries;

namespace LegendaryTools.Common.Core.Patterns.ECS.Worlds
{
    /// <summary>
    /// API sugar layer for gameplay code (non-invasive).
    /// </summary>
    public sealed partial class World
    {
        /// <summary>
        /// Gameplay-friendly commands facade:
        /// - Outside update: immediate structural changes.
        /// - During update: routes to ECB.
        /// </summary>
        public WorldCommands Commands => new(this);

        /// <summary>
        /// Query helpers (keeps gameplay code short and readable).
        /// </summary>
        public Query QueryAll<T1>() where T1 : struct
        {
            return QueryFactory.All<T1>(this);
        }

        public Query QueryAll<T1, T2>()
            where T1 : struct
            where T2 : struct
        {
            return QueryFactory.All<T1, T2>(this);
        }

        public Query QueryAll<T1, T2, T3>()
            where T1 : struct
            where T2 : struct
            where T3 : struct
        {
            return QueryFactory.All<T1, T2, T3>(this);
        }

        public Query QueryAllNone<T1, TNone>()
            where T1 : struct
            where TNone : struct
        {
            return QueryFactory.AllNone<T1, TNone>(this);
        }

        public Query QueryAllNone<T1, T2, TNone>()
            where T1 : struct
            where T2 : struct
            where TNone : struct
        {
            return QueryFactory.AllNone<T1, T2, TNone>(this);
        }
    }
}