using UnityEngine;
using UnityEngine.UI;

namespace LegendaryTools.UI
{
    [AddComponentMenu("UI/Effects/Gradient")]
    public class UIGradient : BaseMeshEffect
    {
        public Color BottonLeft = Color.black;
        public Color BottonRight = Color.black;
        public Color TopLeft = Color.white;
        public Color TopRight = Color.white;
        private float xMax;

        private float xMin;
        private float yMax;
        private float yMin;

        public override void ModifyMesh(VertexHelper helper)
        {
            if (!IsActive() || helper.currentVertCount == 0)
            {
                return;
            }

            UIVertex v = new UIVertex();
            xMin = xMin = yMin = yMax = 0;
            for (int i = 0; i < helper.currentVertCount; i++)
            {
                helper.PopulateUIVertex(ref v, i);

                if (v.position.y <= yMin)
                {
                    yMin = v.position.y;
                }

                if (v.position.y >= yMax)
                {
                    yMax = v.position.y;
                }

                if (v.position.x <= xMin)
                {
                    xMin = v.position.x;
                }

                if (v.position.x >= xMax)
                {
                    xMax = v.position.x;
                }
            }

            for (int i = 0; i < helper.currentVertCount; i++)
            {
                helper.PopulateUIVertex(ref v, i);

                v.color = ColorUtil.BilinearColor(BottonLeft, BottonRight, TopLeft, TopRight,
                    v.position.x.Remap(xMin, xMax, 0, 1), v.position.y.Remap(yMin, yMax, 0, 1));

                helper.SetUIVertex(v, i);
            }
        }
    }
}