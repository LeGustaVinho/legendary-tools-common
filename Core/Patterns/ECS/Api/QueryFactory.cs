using System;
using LegendaryTools.Common.Core.Patterns.ECS.Components;
using LegendaryTools.Common.Core.Patterns.ECS.Queries;
using LegendaryTools.Common.Core.Patterns.ECS.Worlds;

namespace LegendaryTools.Common.Core.Patterns.ECS.Api
{
    /// <summary>
    /// Small helpers to create queries without manual ComponentTypeId arrays.
    /// </summary>
    public static class QueryFactory
    {
        public static Query All<T1>(World world)
            where T1 : struct
        {
            Span<ComponentTypeId> all = stackalloc ComponentTypeId[1];
            all[0] = world.GetComponentTypeId<T1>();

            return new Query(all, ReadOnlySpan<ComponentTypeId>.Empty);
        }

        public static Query All<T1, T2>(World world)
            where T1 : struct
            where T2 : struct
        {
            Span<ComponentTypeId> all = stackalloc ComponentTypeId[2];
            all[0] = world.GetComponentTypeId<T1>();
            all[1] = world.GetComponentTypeId<T2>();

            return new Query(all, ReadOnlySpan<ComponentTypeId>.Empty);
        }

        public static Query All<T1, T2, T3>(World world)
            where T1 : struct
            where T2 : struct
            where T3 : struct
        {
            Span<ComponentTypeId> all = stackalloc ComponentTypeId[3];
            all[0] = world.GetComponentTypeId<T1>();
            all[1] = world.GetComponentTypeId<T2>();
            all[2] = world.GetComponentTypeId<T3>();

            return new Query(all, ReadOnlySpan<ComponentTypeId>.Empty);
        }

        public static Query AllNone<T1, TNone>(World world)
            where T1 : struct
            where TNone : struct
        {
            Span<ComponentTypeId> all = stackalloc ComponentTypeId[1];
            all[0] = world.GetComponentTypeId<T1>();

            Span<ComponentTypeId> none = stackalloc ComponentTypeId[1];
            none[0] = world.GetComponentTypeId<TNone>();

            return new Query(all, none);
        }

        public static Query AllNone<T1, T2, TNone>(World world)
            where T1 : struct
            where T2 : struct
            where TNone : struct
        {
            Span<ComponentTypeId> all = stackalloc ComponentTypeId[2];
            all[0] = world.GetComponentTypeId<T1>();
            all[1] = world.GetComponentTypeId<T2>();

            Span<ComponentTypeId> none = stackalloc ComponentTypeId[1];
            none[0] = world.GetComponentTypeId<TNone>();

            return new Query(all, none);
        }
    }
}