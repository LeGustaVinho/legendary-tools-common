namespace LegendaryTools
{
    public class UnityBehaviour :
#if ODIN_INSPECTOR
        Sirenix.OdinInspector.SerializedMonoBehaviour
#else
        UnityEngine.MonoBehaviour
#endif
    {
        
    }
}