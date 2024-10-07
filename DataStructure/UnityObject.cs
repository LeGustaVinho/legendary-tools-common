namespace LegendaryTools
{
    public class UnityObject : 
#if ODIN_INSPECTOR
        Sirenix.OdinInspector.SerializedScriptableObject
#else
        UnityEngine.ScriptableObject
#endif
    {
        
    }
}