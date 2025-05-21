namespace LegendaryTools.Actor
{
    public abstract class ActorConfig : 
#if ODIN_INSPECTOR
        Sirenix.OdinInspector.SerializedScriptableObject
#else
        UnityEngine.ScriptableObject
#endif
    {
        
    }
}