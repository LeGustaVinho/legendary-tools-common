using System;

namespace LegendaryTools
{
    public static class ActionExtension 
    {
        public static void FireInMainThread(this Action action)
        {
            if (action != null)
            {
                MonoBehaviourFacade.Instance.Execute(action);
            }
        }

        public static void FireInMainThread<A>(this Action<A> action, A arg)
        {
            if (action != null)
            {
                MonoBehaviourFacade.Instance.Execute(() =>
                {
                    action(arg);
                });
            }
        }

        public static void FireInMainThread<A, B>(this Action<A, B> action, A arg1, B arg2)
        {
            if (action != null)
            {
                MonoBehaviourFacade.Instance.Execute(() =>
                {
                    action(arg1, arg2);
                });
            }
        }

        public static void FireInMainThread<A, B, C>(this Action<A, B, C> action, A arg1, B arg2, C arg3)
        {
            if (action != null)
            {
                MonoBehaviourFacade.Instance.Execute(() =>
                {
                    action(arg1, arg2, arg3);
                });
            }
        }
        
        public static void FireInMainThread<A, B, C, D>(this Action<A, B, C, D> action, A arg1, B arg2, C arg3, D arg4)
        {
            if (action != null)
            {
                MonoBehaviourFacade.Instance.Execute(() =>
                {
                    action(arg1, arg2, arg3, arg4);
                });
            }
        }
        
        public static void FireInMainThread<A, B, C, D, E>(this Action<A, B, C, D, E> action, A arg1, B arg2, C arg3, D arg4, E arg5)
        {
            if (action != null)
            {
                MonoBehaviourFacade.Instance.Execute(() =>
                {
                    action(arg1, arg2, arg3, arg4, arg5);
                });
            }
        }
    }
}