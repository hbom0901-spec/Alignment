// Alignment.Core/AffineLinearSolver.cs
using System;
using System.Collections.Generic;
using MathNet.Numerics.LinearAlgebra;
using System.Diagnostics;

namespace Alignment.Core
{
    // 6 參數：X = a*x + b*y + c,  Y = d*x + e*y + f

    public static class AffineLinearSolver
    {
        private const bool UseLegacySolver = false;

        public static VectorX SolveVectorX(
            IReadOnlyList<P3> ccdPts,
            IReadOnlyList<P3> realPts,
            out double[] residuals,
            out double rmse)
        {
            if (ccdPts == null || realPts == null)
            {
                throw new ArgumentNullException();
            }

            int n = ccdPts.Count;
            if (n != realPts.Count || n < 3)
            {
                throw new ArgumentException("Need ≥3 pairs");
            }

            double[,] A = new double[2 * n, 6];
            double[] L = new double[2 * n];

            for (int i = 0; i < n; i++)
            {
                P3 s = ccdPts[i];
                P3 t = realPts[i];
                int r = 2 * i;

                // X 方程
                A[r, 0] = s.X; A[r, 1] = s.Y; A[r, 2] = 1;
                A[r, 3] = 0; A[r, 4] = 0; A[r, 5] = 0;
                L[r] = t.X;

                // Y 方程
                r++;
                A[r, 0] = 0; A[r, 1] = 0; A[r, 2] = 0;
                A[r, 3] = s.X; A[r, 4] = s.Y; A[r, 5] = 1;
                L[r] = t.Y;
            }

            Matrix<double> M = Matrix<double>.Build.DenseOfArray(A);
            Vector<double> b = Vector<double>.Build.DenseOfArray(L);

            Vector<double> x;

            if (UseLegacySolver)
            {
                // 舊版: 直接解 (相當於 LU 分解)
                x = M.Solve(b);
            }
            else
            {
                // 新版: 先試 SVD，失敗退 QR
                try { x = M.Svd(true).Solve(b); }
                catch { x = M.QR().Solve(b); }
            }

            VectorX theta = new VectorX
            {
                a = x[0],
                b = x[1],
                c = x[2],
                d = x[3],
                e = x[4],
                f = x[5]
            };

            // 殘差與 RMSE
            Vector<double> rvec = M * x - b;
            residuals = rvec.ToArray();
            double sse = 0;
            for (int i = 0; i < residuals.Length; i++)
            {
                sse += residuals[i] * residuals[i];
            }

            rmse = Math.Sqrt(sse / n);

            return theta;
        }
        // 簡化多載
        public static VectorX SolveVectorX(IReadOnlyList<P3> ccdPts, IReadOnlyList<P3> robotPts)
        {
            double[] _;
            return SolveVectorX(ccdPts, robotPts, out _, out double _);
        }
    }

    public static class VectorXOps
    {
        public static P3 Transform(VectorX t, P3 p)
        {
            return new P3
            {
                X = (t.a * p.X) + (t.b * p.Y) + t.c,
                Y = (t.d * p.X) + (t.e * p.Y) + t.f,
                U = p.U
            };
        }

        public static VectorX Invert(VectorX t)
        {
            double det = (t.a * t.e) - (t.b * t.d);
            if (Math.Abs(det) < 1e-12) throw new InvalidOperationException("Singular");
            double a = t.e / det;
            double b = -t.b / det;
            double d = -t.d / det;
            double e = t.a / det;
            double c = -((a * t.c) + (b * t.f));
            double f = -((d * t.c) + (e * t.f));
            return new VectorX { a = a, b = b, c = c, d = d, e = e, f = f };
        }

        // 連續投射
        public static VectorX Compose(VectorX t1, VectorX t2)
        {
            return new VectorX
            {
                a = (t1.a * t2.a) + (t1.b * t2.d),
                b = (t1.a * t2.b) + (t1.b * t2.e),
                c = (t1.a * t2.c) + (t1.b * t2.f) + t1.c,
                d = (t1.d * t2.a) + (t1.e * t2.d),
                e = (t1.d * t2.b) + (t1.e * t2.e),
                f = (t1.d * t2.c) + (t1.e * t2.f) + t1.f
            };
        }
    }

    public static class AffineDecompose
    {
        // Theta=atan2(d,a); Sx=a/cosθ or d/sinθ; Sy=e/cosθ or -b/sinθ
        public static CalibInfo GetCalibInfo(VectorX v)
        {
            double theta = Math.Atan2(v.d, v.a);
            double sintheda = Math.Sin(theta);
            double costheta = Math.Cos(theta);

            // 1) 旋轉 + 縮放 (你原本的邏輯)
            double sx, sy;
            if (Math.Abs(costheta) >= Math.Abs(sintheda))
            {
                sx = v.a / costheta;
                sy = v.e / costheta;
            }
            else
            {
                sx = v.d / sintheda;
                sy = -v.b / sintheda;
            }

            //if (v.a * v.e - v.b * v.d < 0)
            //    sy = -sy;

            // 2) shear 分解 (修正版)
            double a1 = (costheta * v.a) + (sintheda * v.d);
            double b1 = (costheta * v.b) + (sintheda * v.e);
            double d1 = (-sintheda * v.a) + (costheta * v.d);
            double e1 = (-sintheda * v.b) + (costheta * v.e);

            double ssx = Math.Sqrt((a1 * a1) + (d1 * d1));
            double shear = ((a1 * b1) + (d1 * e1)) / Math.Max(1e-12, ssx * ssx);
            double ssy = Math.Sqrt( Math.Max(0.0, (b1 * b1) + (e1 * e1) - (shear * shear * ssx * ssx) ) );

            return new CalibInfo
            {
                ThetaDeg = theta * 180.0 / Math.PI,
                Sx = sx,
                Sy = sy,
                Shear = shear,
                sSx = ssx,
                sSy = ssy
            };
        }

    }
}
