using System;
using UnityEngine;

namespace LegendaryTools.UI
{
    [Serializable]
    public class DraggingObject
    {
        public int ID;
        public UIDrag Object;
        public bool OnDrop;

        public bool OnEndDrag;
        public RectTransform Plane;

        public DraggingObject(int id, UIDrag theObject, RectTransform plane = null)
        {
            ID = id;
            Object = theObject;
            Plane = plane;
        }
    }
}