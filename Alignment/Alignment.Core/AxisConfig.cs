using System;

namespace Alignment.Core
{
    public enum AxisDir { Right, Left, Up, Down }
    public enum RotDir { CCW, CW }

    public static class AxisConfig
    {
        public static int ComputeSignU(AxisDir xPos, AxisDir yPos, RotDir uRot)
        {
            (int x, int y) = ToVec(xPos); // 影像座標：Right=(+1,0), Down=(0,+1)

            int dot = (x * ToVec(yPos).x) + (y * ToVec(yPos).y);
            if (dot != 0)
                throw new ArgumentException(
                    $"Invalid axis config: XPositive={xPos}, YPositive={yPos}. " +
                    "X and Y must be orthogonal (choose one horizontal and one vertical).");

            int det = (x * ToVec(yPos).y) - (y * ToVec(yPos).x); // ±1
            if (det == 0)
            {
                throw new ArgumentException("Degenerate axis basis.");
            }

            int rot = (uRot == RotDir.CCW) ? +1 : -1; // 內部以CCW為正
            return det * rot;
        }

        public static (int x, int y) ToVec(AxisDir dir)
        {
            switch (dir)
            {
                case AxisDir.Right: return (+1, 0);
                case AxisDir.Left: return (-1, 0);
                case AxisDir.Down: return (0, +1); // 影像Y向下為正
                case AxisDir.Up: return (0, -1);
                default: throw new ArgumentOutOfRangeException(nameof(dir));
            }
        }
    }
}
