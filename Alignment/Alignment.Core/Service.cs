using System;
using System.Collections.Generic;
using System.Linq;
using MathNet.Numerics.LinearAlgebra;

namespace Alignment.Core
{
    public interface IAlignmentService
    {
        void CalibrateAffine(string connection, string camera, IReadOnlyList<P3> robotPts, IReadOnlyList<P3> cameraPts);
         (P3 center, double rmse) ComputeRotationCenter(
            RCMethod method,
            IReadOnlyList<P3> ccdPts,
            IReadOnlyList<P3> realPtsOrNull);
        void RegisterGolden(string connection, string camera, IReadOnlyList<P3> pixelGolden, P3 realGolden);
        P3 ComputeOffset(string connection, string camera);
        List<P3> GetCalibOffset(string connection);
        P3 RotatePoint(P3 center, P3 p, double angleDeg);

        /// <summary>
        /// 多點校正整合：
        /// - 前 affineCount 組 (ccd, rob) 用來解仿射並寫入 PixelToReal/RealToPixel/Origin
        /// - 接下來 rotationCount 組用來算旋轉中心並寫入 RotationCenters
        /// 回傳 AffineDecompose 的結果，並透過 out 帶出 RMSE 與旋心資訊
        /// </summary>
        CalibInfo CalibrateFromPairs(
            string connection,
            string camera,
            IReadOnlyList<(P3 ccd, P3 rob)> pairs,
            int affineCount,
            int rotationCount,
            out double affineRmse,
            out P3 rotationCenter,
            out double rotationRmse);
        void ClearBuffers();
        /// <summary>
        /// 清除指定連線的所有校正資料（矩陣 / golden / 旋心等）
        /// </summary>
        void ClearCalibration(string connection);
        AlignmentState Snapshot();
    }

    public sealed class AlignmentService : IAlignmentService
    {
        private readonly AlignmentState _s;
        private readonly IAlignmentConfig _config;

        public AlignmentService(AlignmentState state, IAlignmentConfig config)
        { 
            _s = state ?? new AlignmentState();
            _config = config; 
        }

        public void CalibrateAffine(string connection, string camera, IReadOnlyList<P3> robotPts, IReadOnlyList<P3> cameraPts)
        {
            if (!_s.ByConn.TryGetValue(connection, out var scope))
                _s.ByConn[connection] = scope = new CalibData();

            var vx = AffineLinearSolver.SolveVectorX(cameraPts, robotPts, out _, out _); // Pixel->Real
            scope.PixelToReal[camera] = vx;
            scope.RealToPixel[camera] = VectorXOps.Invert(vx);
            if (robotPts.Count > 0) scope.Origin = robotPts[0].Clone();
        }

        /// <summary>
        /// 整合版多點校正：
        /// - 前 affineCount 筆 pairs 拿來解仿射
        /// - 接下來 rotationCount 筆 pairs 拿來算旋轉中心
        /// 同時更新內部 AlignmentState 的 CalibData。
        /// </summary>
        public CalibInfo CalibrateFromPairs(
            string connection,
            string camera,
            IReadOnlyList<(P3 ccd, P3 rob)> pairs,
            int affineCount,
            int rotationCount,
            out double affineRmse,
            out P3 rotationCenter,
            out double rotationRmse)
        {
            if (pairs == null)
            {
                throw new ArgumentNullException(nameof(pairs));
            }

            if (affineCount < 3)
            {
                throw new ArgumentOutOfRangeException(nameof(affineCount));
            }

            if (rotationCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(rotationCount));
            }

            if (pairs.Count < affineCount + rotationCount)
            {
                throw new ArgumentException("Not enough pairs for requested affine and rotation counts.", nameof(pairs));
            }

            if (!_s.ByConn.TryGetValue(connection, out var scope))
            {
                _s.ByConn[connection] = scope = new CalibData();
            }

            // [新增] 把所有 pairs 映射到 UI 用的 RobotPoints / CameraPoints
            _s.RobotPoints.Clear();
            foreach (KeyValuePair<string, List<P3>> kv in _s.CameraPoints)
            {
                kv.Value.Clear();
            }

            if (!_s.CameraPoints.TryGetValue(camera, out var uiCamList))
            {
                uiCamList = new List<P3>();
                _s.CameraPoints[camera] = uiCamList;
            }

            foreach ((P3 ccd, P3 rob) in pairs)
            {
                _s.RobotPoints.Add(rob.Clone());
                uiCamList.Add(ccd.Clone());
            }

            // 1) 仿射：前 affineCount 筆
            List<P3> ccdPts = new List<P3>(affineCount);
            List<P3> realPts = new List<P3>(affineCount);
            for (int i = 0; i < affineCount; i++)
            {
                (P3 ccd, P3 rob) = pairs[i];
                ccdPts.Add(ccd);
                realPts.Add(rob);
            }

            VectorX vx = AffineLinearSolver.SolveVectorX(ccdPts, realPts, out var residuals, out affineRmse);
            CalibInfo info = AffineDecompose.GetCalibInfo(vx);
            VectorX inv = VectorXOps.Invert(vx);

            scope.PixelToReal[camera] = vx;
            scope.RealToPixel[camera] = inv;
            if (realPts.Count > 0)
            {
                scope.Origin = realPts[0].Clone();
            }

            // 2) 旋轉中心：接下來 rotationCount 筆（如果有）
            rotationCenter = new P3();
            rotationRmse = 0.0;

            if (rotationCount > 0)
            {
                List<P3> rcCcd = new List<P3>(rotationCount);
                List<P3> rcReal = new List<P3>(rotationCount);

                for (int i = 0; i < rotationCount; i++)
                {
                    int idx = affineCount + i;
                    if (idx >= pairs.Count) break;

                    (P3 ccd, P3 rob) = pairs[idx];
                    rcCcd.Add(ccd);
                    rcReal.Add(rob);
                }
                AlignmentParams para = _config.LoadParams();
                int signU = AxisConfig.ComputeSignU(para.XPositive, para.YPositive, para.URotation);
                List<P3> realAdj = rcReal
                .Select(p => new P3 { X = p.X, Y = p.Y, U = signU * p.U })
                .ToList();
                if (rcCcd.Count >= 2)
                {
                    // 這裡沿用 Core 的 CalculateRotateCenter 演算法
                    (P3 center, double rmse) = RotationCenter.CalculateRotateCenter(rcCcd, realAdj);
                    rotationCenter.X = center.X;
                    rotationCenter.Y = center.Y;
                    rotationCenter.U = center.U;
                    rotationRmse = rmse;
                    scope.RotationCenters[camera] = rotationCenter.Clone();
                }
            }

            return info;
        }


        public (P3 center, double rmse) ComputeRotationCenter(
            RCMethod method,
            IReadOnlyList<P3> ccdPts,
            IReadOnlyList<P3> realPts)
        {
            return RotationCenter.CalculateRotateCenter(ccdPts, realPts, method);
        }

        public void RegisterGolden(string connection, string camera, IReadOnlyList<P3> pixelGolden, P3 realGolden)
        {
            if (!_s.ByConn.TryGetValue(connection, out var scope))
                _s.ByConn[connection] = scope = new CalibData();
            scope.PixelGolden[camera] = new List<P3>(pixelGolden.Select(p => p.Clone()));
            scope.RealGolden = realGolden.Clone();
        }

        public P3 ComputeOffset(string connection, string camera)
        {
            if (!_s.ByConn.TryGetValue(connection, out var scope)
             || !scope.PixelToReal.TryGetValue(camera, out var p2r)
             || !scope.PixelGolden.TryGetValue(camera, out var pg) || pg.Count == 0)
                throw new InvalidOperationException("Missing mapping or golden");

            var gPix = pg[0];
            var gReal = VectorXOps.Transform(p2r, gPix);

            var diff = new P3
            {
                X = scope.RealGolden.X - gReal.X,
                Y = scope.RealGolden.Y - gReal.Y,
                U = scope.RealGolden.U - gReal.U
            };
            diff.X += _s.Params.OffsetTrim.X; diff.Y += _s.Params.OffsetTrim.Y; diff.U += _s.Params.OffsetTrim.U;
            diff.X = Clamp(diff.X, -_s.Params.OffsetLimit.X, _s.Params.OffsetLimit.X);
            diff.Y = Clamp(diff.Y, -_s.Params.OffsetLimit.Y, _s.Params.OffsetLimit.Y);
            diff.U = Clamp(diff.U, -_s.Params.OffsetLimit.U, _s.Params.OffsetLimit.U);
            return diff;
        }

        public List<P3> GetCalibOffset(string connection)
        {
            var list = new List<P3>();
            var mv = _s.Params.CalibMove;
            foreach (var m in _s.Const.CalibMoveMatrix)
                list.Add(new P3 { X = m.X * mv.X, Y = m.Y * mv.Y, U = m.U * mv.U });
            return list;
        }

        public P3 RotatePoint(P3 center, P3 p, double angleDeg)
        {
            double r = angleDeg * Math.PI / 180.0;
            double cos = Math.Cos(r), sin = Math.Sin(r);
            double dx = p.X - center.X, dy = p.Y - center.Y;
            return new P3 { X = cos * dx - sin * dy + center.X, Y = sin * dx + cos * dy + center.Y, U = p.U };
        }

        public void ClearBuffers()
        {
            _s.RobotPoints.Clear();
            foreach (var kv in _s.CameraPoints) kv.Value.Clear();
        }
        public void ClearCalibration(string connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            if (!_s.ByConn.TryGetValue(connection, out var c))
                return;

            c.PixelToReal.Clear();
            c.RealToPixel.Clear();
            c.PixelGolden.Clear();
            c.RotationCenters.Clear();
            c.RealGolden = new P3();
        }
        public AlignmentState Snapshot() => _s;

        static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
