using System;
using System.Collections.Generic;

namespace Alignment.Core
{
    public static class CircleFit
    {
        // Kåsa 圓擬合法
        public static (P3 center, double radius, double rmse) FitKasa(IReadOnlyList<P3> pts)
        {
            if (pts == null || pts.Count < 3)
                throw new ArgumentException("Need ≥3 points.");

            int n = pts.Count;
            double sumX = 0, sumY = 0, sumX2 = 0, sumY2 = 0, sumXY = 0;
            double sumX3 = 0, sumY3 = 0, sumX1Y2 = 0, sumX2Y1 = 0;

            foreach (var p in pts)
            {
                double x = p.X, y = p.Y;
                double x2 = x * x, y2 = y * y;
                sumX += x; sumY += y;
                sumX2 += x2; sumY2 += y2;
                sumXY += x * y;
                sumX3 += x2 * x; sumY3 += y2 * y;
                sumX1Y2 += x * y2; sumX2Y1 += x2 * y;
            }

            double C = n * sumX2 - sumX * sumX;
            double D = n * sumXY - sumX * sumY;
            double E = n * sumX3 + n * sumX1Y2 - (sumX2 + sumY2) * sumX;
            double G = n * sumY2 - sumY * sumY;
            double H = n * sumX2Y1 + n * sumY3 - (sumX2 + sumY2) * sumY;

            double denom = 2 * (C * G - D * D);
            if (Math.Abs(denom) < 1e-12)
                throw new InvalidOperationException("Degenerate circle fit.");

            double cx = (G * E - D * H) / denom;
            double cy = (C * H - D * E) / denom;

            // 半徑與RMSE
            double rsum = 0, errsum = 0;
            foreach (var p in pts)
            {
                double dx = p.X - cx, dy = p.Y - cy;
                double r = Math.Sqrt(dx * dx + dy * dy);
                rsum += r;
            }
            double rmean = rsum / n;
            foreach (var p in pts)
            {
                double dx = p.X - cx, dy = p.Y - cy;
                double r = Math.Sqrt(dx * dx + dy * dy);
                double e = r - rmean;
                errsum += e * e;
            }

            return (new P3 { X = cx, Y = cy, U = 0 }, rmean, Math.Sqrt(errsum / n));
        }
    }
}
