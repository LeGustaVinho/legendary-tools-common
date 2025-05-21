using System;
using UnityEngine;

namespace LegendaryTools
{
    public class PrintSaver : MonoBehaviour
    {
#if UNITY_EDITOR
        private static readonly string IMAGE_FORMAT = ".png";

        public KeyCode PrintKey = KeyCode.F12;
        
        private void Start()
        {
            DontDestroyOnLoad(this);
        }
        
        private void Update()
        {
            if (UnityEngine.Input.GetKeyUp(PrintKey))
            {
                ScreenCapture.CaptureScreenshot(GenerateUID() + IMAGE_FORMAT);
            }
        }

        public static string GenerateUID()
        {
            return string.Format("{0: yyyyMMddHHmmssffff}", DateTime.Now);
        }
#endif
    }
}