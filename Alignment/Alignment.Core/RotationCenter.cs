using System;
using System.Collections.Generic;
using System.Linq;
using MathNet.Numerics.LinearAlgebra;

namespace Alignment.Core
{
    public static class RotationCenter
    {
        public static (P3 center, double rmse) CalculateRotateCenter( IReadOnlyList<P3> ccdPts, IReadOnlyList<P3> realPts) => CalculateRotateCenter(ccdPts, realPts, RCMethod.AnglePair);

        public static (P3 center, double rmse) CalculateRotateCenter(IReadOnlyList<P3> ccdPts, IReadOnlyList<P3> realPts, RCMethod method) 
        {
            switch (method)
            {
                case RCMethod.AnglePair:
                    return CalculateByPairs(ccdPts, realPts);

                case RCMethod.CircleFit:
                    return CalculateByFitKasa(ccdPts);

                default:
                    throw new ArgumentOutOfRangeException(nameof(method), method, null);
            }
        }

        public static (P3 center, double rmse) CalculateByPairs( IReadOnlyList<P3> ccdPts, IReadOnlyList<P3> realPts)
        {
            if (ccdPts == null || realPts == null)
                throw new ArgumentNullException();
            int n = ccdPts.Count;
            if (n != realPts.Count || n < 2)
                throw new ArgumentException("point counts must match, need ≥2 pairs");

            var centers = new List<P3>();
            var rmses = new List<double>();

            // 逐對計算旋轉中心 (i < j)
            for (int i = 0; i < n - 1; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    double angleI = realPts[i].U;
                    double angleJ = realPts[j].U;

                    // 這一對點的角度差（單位：度）
                    double deltaDeg = angleJ - angleI;
                    if (Math.Abs(deltaDeg) < 1e-6)
                        continue; // 忽略角度太接近的組合，避免奇異

                    // ---------------------------------------------
                    // 將「兩點 + 已知旋轉角度」轉成線性方程組 Ax = v
                    //
                    // 幾何模型：
                    //   問題：已知 P (原角度)、Q (旋轉後) 與旋轉角度 θ，
                    //        求旋轉中心 C，使得：
                    //
                    //     Q ≈ R(θ) * (P - C) + C
                    //
                    //   將式子整理，可寫成：
                    //
                    //     (I - R) * C  ≈  Q - R * P
                    //
                    //   其中 (I-R) 是 2x2 矩陣，C 是未知向量 (cx, cy)^T，
                    //   右邊 Q - R*P 是已知向量。
                    //
                    //   對每一對點 (P_i, Q_i) 都給出 2 個線性方程，
                    //   多對點時可累加成最小平方問題：
                    //
                    //     A^T A x = A^T v
                    //
                    //   我們這裡是在「一對點」的情況下解出中心，
                    //   但仍沿用同一套 A^T A / A^T v 的推導公式，
                    //   易於和多對情況統一、也便於日後擴充。
                    // ---------------------------------------------

                    // 將角度轉成弧度，計算旋轉矩陣的 cos/sin
                    double r = deltaDeg * Math.PI / 180.0;
                    double c = Math.Cos(r);
                    double s = Math.Sin(r);

                    // (I - R) 的 2x2 元素
                    double a00 = 1 - c;
                    double a01 = s;
                    double a10 = -s;
                    double a11 = 1 - c;

                    // P = ccdPts[i]（基準角度的 CCD 點）
                    // Q = ccdPts[j]（另一角度的 CCD 點）
                    double ax = ccdPts[i].X;
                    double ay = ccdPts[i].Y;
                    double bx = ccdPts[j].X;
                    double by = ccdPts[j].Y;

                    // 先將 P 旋轉 θ 得到 R * P
                    double rx = c * ax - s * ay;
                    double ry = s * ax + c * ay;

                    // v = Q - R * P
                    double vx = bx - rx;
                    double vy = by - ry;

                    // 對「單一對點」累計 A^T A 與 A^T v 的元素
                    //   A 是 2x2、x=(cx, cy)^T、v=(vx, vy)^T，
                    //   根據推導可得：
                    //
                    //   ata00 = Σ(a00^2 + a10^2)
                    //   ata01 = Σ(a00*a01 + a10*a11)
                    //   ata11 = Σ(a01^2 + a11^2)
                    //   atv0  = Σ(a00*vx  + a10*vy)
                    //   atv1  = Σ(a01*vx  + a11*vy)
                    //
                    double ata00 = a00 * a00 + a10 * a10;
                    double ata01 = a00 * a01 + a10 * a11;
                    double ata11 = a01 * a01 + a11 * a11;
                    double atv0 = a00 * vx + a10 * vy;
                    double atv1 = a01 * vx + a11 * vy;

                    // 解 2x2 線性系統：
                    //   [ata00 ata01][cx] = [atv0]
                    //   [ata01 ata11][cy]   [atv1]
                    //
                    double det = ata00 * ata11 - ata01 * ata01;
                    if (Math.Abs(det) < 1e-18)
                        continue; // 這一對太接近奇異（角度太小或幾何退化），略過

                    double cx = (atv0 * ata11 - ata01 * atv1) / det;
                    double cy = (-atv0 * ata01 + ata00 * atv1) / det;

                    // 估計此對點的殘差（用來計算每一對的 RMSE）
                    //
                    // 理論上：
                    //   Q_hat = R * P + (I - R) * C
                    //
                    double bx_hat = c * ax - s * ay + a00 * cx + a01 * cy;
                    double by_hat = s * ax + c * ay + a10 * cx + a11 * cy;

                    double dx = bx - bx_hat;
                    double dy = by - by_hat;
                    double sse = dx * dx + dy * dy;

                    double rmse = Math.Sqrt(sse); // 一對點 → 當作 2 個方程，這裡簡單取 sqrt(sse)

                    centers.Add(new P3 { X = cx, Y = cy, U = 0 });
                    rmses.Add(rmse);
                }
            }

            if (centers.Count == 0)
                throw new InvalidOperationException("No valid angle pairs to compute rotation center.");

            // 對所有 pair 求出的中心點做平均，平滑量測誤差
            double cxAvg = centers.Average(p => p.X);
            double cyAvg = centers.Average(p => p.Y);

            double avgRmse = rmses.Count > 0 ? rmses.Average() : 0;

            return (new P3 { X = cxAvg, Y = cyAvg, U = 0 }, avgRmse);
        }

        public static (P3 center,  double rmse) CalculateByFitKasa(IReadOnlyList<P3> pts)
        {
            if (pts == null || pts.Count < 3) throw new ArgumentException("Need ≥3 points");
            int n = pts.Count;

            // 均值中心化提升條件數
            double mx = 0, my = 0;
            for (int i = 0; i < n; i++) { mx += pts[i].X; my += pts[i].Y; }
            mx /= n; my /= n;

            double Sx = 0, Sy = 0, Sxx = 0, Syy = 0, Sxy = 0, Sz = 0, Sxz = 0, Syz = 0;
            for (int i = 0; i < n; i++)
            {
                double x = pts[i].X - mx, y = pts[i].Y - my, z = x * x + y * y;
                Sx += x; Sy += y; Sxx += x * x; Syy += y * y; Sxy += x * y; Sz += z; Sxz += x * z; Syz += y * z;
            }

            // 解 3x3 正規方程
            var A = Matrix<double>.Build.DenseOfArray(new double[,]
            {
                { Sxx, Sxy, Sx },
                { Sxy, Syy, Sy },
                { Sx , Sy , n  }
            });
            var b = Vector<double>.Build.DenseOfArray(new double[] { -Sxz, -Syz, -Sz });

            var xsol = A.Solve(b); // [A1, B1, C1]
            double A1 = xsol[0], B1 = xsol[1], C1 = xsol[2];

            double cx0 = -A1 / 2.0, cy0 = -B1 / 2.0;
            double cx = cx0 + mx, cy = cy0 + my;
            double r = Math.Sqrt(Math.Max(0.0, cx0 * cx0 + cy0 * cy0 - C1));

            double sse = 0;
            for (int i = 0; i < n; i++)
            {
                double dx = pts[i].X - cx, dy = pts[i].Y - cy;
                double err = Math.Sqrt(dx * dx + dy * dy) - r;
                sse += err * err;
            }
            return (new P3 { X = cx, Y = cy, U = 0 },  Math.Sqrt(sse / n));
        }

    }
}
