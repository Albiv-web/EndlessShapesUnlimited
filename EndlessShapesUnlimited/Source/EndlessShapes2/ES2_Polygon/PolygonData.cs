using System.Collections.Generic;
using UnityEngine;

namespace EndlessShapes2.Polygon
{
    public class PolygonData
    {
        public PolygonType PolyType { get; private set; }

        public SideData[] Sides { get; private set; }

        public Vector3 NormalVector { get; private set; }

        public Vector2 UV { get; private set; }

        public int SourceLine { get; private set; }

        public PolygonData(
            PolygonType polygonType,
            int[] indexs,
            List<Vector3> vertices,
            Vector2 uv,
            int sourceLine = 0)
        {
            PolyType = polygonType;
            Sides = PolygonDataControl.GenerateSides(indexs, vertices);
            UV = uv;
            SourceLine = sourceLine;

            switch (polygonType)
            {
                case PolygonType.Line:
                    NormalVector = Vector3.zero;
                    break;
                default:
                    NormalVector = CalculateNormal(Sides);
                    break;
            }
        }

        private static Vector3 CalculateNormal(SideData[] sides)
        {
            Vector3 normal = Vector3.zero;
            for (int index = 0; index < sides.Length; index++)
            {
                Vector3 current = sides[index].OriginPosition;
                Vector3 next = sides[index].TargetPosition;
                normal.x += (current.y - next.y) * (current.z + next.z);
                normal.y += (current.z - next.z) * (current.x + next.x);
                normal.z += (current.x - next.x) * (current.y + next.y);
            }
            return normal.normalized;
        }
    }
}
