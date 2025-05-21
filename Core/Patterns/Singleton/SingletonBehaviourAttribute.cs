using System;

namespace LegendaryTools
{
    [AttributeUsage(AttributeTargets.Class)]
    public class SingletonBehaviourAttribute : Attribute
    {
        public bool ForceSingleInstance;
        public bool IsPersistent;
        public bool AutoCreateIfNotExists;

        public SingletonBehaviourAttribute(bool forceSingleInstance, bool isPersistent, bool autoCreateIfNotExists)
        {
            ForceSingleInstance = forceSingleInstance;
            IsPersistent = isPersistent;
            AutoCreateIfNotExists = autoCreateIfNotExists;
        }
    }
}