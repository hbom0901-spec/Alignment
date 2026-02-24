using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using Alignment.Core;
using Alignment.WPF.Utils;

namespace Alignment.WPF.ViewModels
{
    public class AlignmentPanelViewModel : INotifyPropertyChanged
    {
        readonly IAlignmentService _svc;
        readonly IAlignmentConfig _cfg;
        readonly AlignmentState _state;

        public Array AxisDirValues => Enum.GetValues(typeof(AxisDir));
        public Array RotDirValues => Enum.GetValues(typeof(RotDir));
        public AlignmentParams Params { get; }
        public ObservableCollection<P3> RobotPoints { get; } = new ObservableCollection<P3>();
        public ObservableCollection<P3> CameraPoints { get; } = new ObservableCollection<P3>();
        public ObservableCollection<P3> CameraToRealPoints { get; } = new ObservableCollection<P3>();
        public IReadOnlyList<RCMethod> RCMethodValues { get; } = Enum.GetValues(typeof(RCMethod)).Cast<RCMethod>().ToList();

        string _connection = "Robot";
        public string Connection { get => _connection; set { _connection = value; OnPropertyChanged(nameof(Connection)); } }

        string _camera = "CCD1";
        public string Camera { get => _camera; set { _camera = value; OnPropertyChanged(nameof(Camera)); } }

        P3 _lastOffset = new P3();
        public P3 LastOffset { get => _lastOffset; private set { _lastOffset = value; OnPropertyChanged(nameof(LastOffset)); OnPropertyChanged(nameof(IsOk)); } }

        public bool IsOk =>
            Math.Abs(LastOffset.X) <= Params.OffsetLimit.X &&
            Math.Abs(LastOffset.Y) <= Params.OffsetLimit.Y &&
            Math.Abs(LastOffset.U) <= Params.OffsetLimit.U;

        public ICommand CmdClear { get; }
        public ICommand CmdSaveParams { get; }
        public ICommand CmdCalibAffine { get; }
        public ICommand CmdComputeOffset { get; }
        public ICommand CmdSeedDemo { get; }

        #region RotateCenterRelated
        int _centerMethodIndex; // 0=CircleFit, 1=WithAngles
        public int CenterMethodIndex
        {
            get => _centerMethodIndex;
            set
            {
                _centerMethodIndex = value;
                OnPropertyChanged(nameof(CenterMethodIndex));
            }
        }
        public ObservableCollection<P3> CenterFitPoints { get; } = new ObservableCollection<P3>();
        public sealed class CenterPairRow : INotifyPropertyChanged
        {
            public P3 A { get; set; } = new P3();
            public P3 B { get; set; } = new P3();
            double _angleDeg;
            public double AngleDeg { get => _angleDeg; set { _angleDeg = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AngleDeg))); } }
            public event PropertyChangedEventHandler PropertyChanged;
        }
        public ObservableCollection<CenterPairRow> CenterPairs { get; } = new ObservableCollection<CenterPairRow>();
        double _centerX, _centerY, _centerRmse;
        public double CenterX { get => _centerX; private set { _centerX = value; OnPropertyChanged(nameof(CenterX)); } }
        public double CenterY { get => _centerY; private set { _centerY = value; OnPropertyChanged(nameof(CenterY)); } }
        public double CenterRmse { get => _centerRmse; private set { _centerRmse = value; OnPropertyChanged(nameof(CenterRmse)); } }
        public ICommand CmdCopyCenterInput { get; }
        public ICommand CmdComputeCenter { get; }
        public ICommand CmdSaveCenter { get; }
        #endregion
        public AlignmentPanelViewModel(IAlignmentService svc, IAlignmentConfig cfg, AlignmentState state)
        {
            _svc = svc ?? throw new ArgumentNullException(nameof(svc));
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            _state = state ?? throw new ArgumentNullException(nameof(state));

            Params = _cfg.LoadParams();
            WireAutoSave(Params);

            CmdClear = new RelayCommand(_ => 
            { 
                _svc.ClearBuffers(); 
                RobotPoints.Clear(); 
                CameraPoints.Clear(); 
                CameraToRealPoints.Clear(); 
                LastOffset = new P3(); 
            });
            CmdSaveParams = new RelayCommand(_ => _cfg.SaveParams(Params));
            CmdCalibAffine = new RelayCommand(_ => CalibAndPreview());
            CmdComputeOffset = new RelayCommand(_ => ComputeOffset());
            CmdSeedDemo = new RelayCommand(_ => SeedDemoData());

            CmdCopyCenterInput = new RelayCommand(_ => 
            { 
                CenterFitPoints.Clear(); 
                foreach (P3 p in CameraPoints) CenterFitPoints.Add(p.Clone()); 
            });
            CmdComputeCenter = new RelayCommand(_ => ComputeCenter());
            CmdSaveCenter = new RelayCommand(_ => SaveCenter(), _ => !double.IsNaN(CenterX) && !double.IsNaN(CenterY));
            CenterMethodIndex = 1;
        }
        public AlignmentPanelViewModel()
      : this(
            new AlignmentService(new AlignmentState(), new JsonAlignmentConfig()),
            new JsonAlignmentConfig(),
            new AlignmentState())
        {
        }

        private void ComputeCenter()
        {
            RCMethod method = Params.RotationMethod;

            if (method == RCMethod.CircleFit)
            {
                // 用 CenterFitPoints，若為空則改用 CameraPoints
                IReadOnlyList<P3> src = CenterFitPoints.Count > 0
                    ? new List<P3>(CenterFitPoints)
                    : new List<P3>(CameraPoints);

                // CircleFit：realPts 傳 null
                (P3 center, double rmse) = _svc.ComputeRotationCenter(
                    method,
                    src,
                    null);

                CenterX = center.X;
                CenterY = center.Y;
                CenterRmse = rmse;
            }
            else
            {
                // 1) 收集 CCD 點：只用 A，代表「該角度下的 CCD 座標」
                var ccdPts = new List<P3>();
                var realPts = new List<P3>();

                foreach (CenterPairRow row in CenterPairs)
                {
                    if (row.A == null)
                        continue; // 保險：如果某列還沒填就略過

                    // A 點 = 此角度下量測到的 CCD 點
                    ccdPts.Add(row.A.Clone());

                    // U = 此點的「絕對角度（度）」，直接用 AngleDeg
                    realPts.Add(new P3
                    {
                        X = 0,              // 這裡 X/Y 不用，留 0 即可
                        Y = 0,
                        U = row.AngleDeg    // 注意：單位 = 度，CalculateByPairs 內部會自己做 deg→rad
                    });
                }

                if (ccdPts.Count < 2)
                {
                    // 可以依你習慣丟例外或顯示訊息
                    throw new InvalidOperationException("AnglePair 需要至少 2 筆點與角度資料");
                }

                // 呼叫共用演算法：裡面會根據 realPts[i].U 的「絕對角度」做差 angleJ - angleI，
                // 然後自己做 deg→rad。
                var (center, rmse) = _svc.ComputeRotationCenter(RCMethod.AnglePair, ccdPts, realPts);

                CenterX = center.X;
                CenterY = center.Y;
                CenterRmse = rmse;
            }
           

        }

        private void SaveCenter()
        {
            //ToDo
        }

        private void WireAutoSave(AlignmentParams p)
        {
            if (p == null) return;

            // 原本：三個 P3 裡任一座標改變就自動存檔
            p.CalibMove.PropertyChanged += (_, __) => _cfg.SaveParams(Params);
            p.OffsetLimit.PropertyChanged += (_, __) => _cfg.SaveParams(Params);
            p.OffsetTrim.PropertyChanged += (_, __) => _cfg.SaveParams(Params);

            // 新增：方向屬性變更時也自動存檔
            p.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(AlignmentParams.XPositive)
                    || e.PropertyName == nameof(AlignmentParams.YPositive)
                    || e.PropertyName == nameof(AlignmentParams.URotation)
                    || e.PropertyName == nameof(AlignmentParams.RotationMethod))
                {
                    _cfg.SaveParams(Params);
                }
            };
        }

        private void CalibAndPreview()
        {
            // 同步 Buffer
            _state.RobotPoints.Clear();
            foreach (var z in RobotPoints) _state.RobotPoints.Add(z.Clone());

            if (!_state.CameraPoints.TryGetValue(Camera, out System.Collections.Generic.List<P3> list))
            {
                list = new System.Collections.Generic.List<P3>();
                _state.CameraPoints[Camera] = list;
            }
            list.Clear();
            foreach (var z in CameraPoints) list.Add(z.Clone());

            // 求解校正
            _svc.CalibrateAffine(Connection, Camera, _state.RobotPoints, list);

            // 預覽 Camera→Real
            CameraToRealPoints.Clear();
            var vx = _state.ByConn[Connection].PixelToReal[Camera]; // VectorX
            foreach (var p in list)
                CameraToRealPoints.Add(VectorXOps.Transform(vx, p));
            CalibInfo rs = AffineDecompose.GetCalibInfo(vx);
           // ThetaDeg = rs.ThetaDeg; Sx = rs.Sx; Sy = rs.Sy;
        }

        public void ReloadFromState(AlignmentState srcState = null)
        {
            // 如果沒指定，就用 VM 內建的 _state（維持原本功能）
            var s = srcState ?? _state;

            // 1) RobotPoints
            RobotPoints.Clear();
            foreach (var p in s.RobotPoints)   // ← 用 s，而不是 _state
                RobotPoints.Add(p.Clone());

            // 2) CameraPoints
            CameraPoints.Clear();
            if (s.CameraPoints.TryGetValue(Camera, out var camList))
            {
                foreach (var p in camList)
                    CameraPoints.Add(p.Clone());
            }

            // 3) CameraToRealPoints 預覽 (如果已經有校正結果)
            CameraToRealPoints.Clear();
            if (s.ByConn.TryGetValue(Connection, out var conn) &&
                conn.PixelToReal != null &&
                conn.PixelToReal.TryGetValue(Camera, out var vx))
            {
                foreach (var p in CameraPoints)
                    CameraToRealPoints.Add(VectorXOps.Transform(vx, p));
            }
            // 4) 旋轉中心相關：CenterFitPoints / CenterPairs / CenterX,Y,Rmse
            // 只使用 CameraPoints[9], [10], [11] 當 CircleFit 輸入
            CenterFitPoints.Clear();

            int[] rcIndices = { 9, 10, 11 };   // 0°, +U, -U 對應的 index

            foreach (int idx in rcIndices)
            {
                if (idx >= 0 && idx < CameraPoints.Count)
                {
                    CenterFitPoints.Add(CameraPoints[idx].Clone());
                }
            }

            // 4-2) AnglePair 用的 pair 與角度（純 UI 預設用）
            CenterPairs.Clear();

            // 至少要有 12 點 (第 13 點回中心不用)
            if (CameraPoints.Count >= 12)
            {
                var p0 = CameraPoints[9];   // 0°
                var pPos = CameraPoints[10];  // +U
                var pNeg = CameraPoints[11];  // -U

                double u = Params.CalibMove?.U ?? 0.0;

                CenterPairs.Add(new CenterPairRow
                {
                    A = p0.Clone(),
                    B = pPos.Clone(),
                    AngleDeg = 0
                });

                CenterPairs.Add(new CenterPairRow
                {
                    A = pNeg.Clone(),
                    B = pNeg.Clone(),
                    AngleDeg = -u
                });

                CenterPairs.Add(new CenterPairRow
                {
                    A = pPos.Clone(),
                    B = pPos.Clone(),
                    AngleDeg =  u
                });
            }

            // 4-3) 旋轉中心結果本身 → 直接從 state 讀，不要重算
            if (s.ByConn.TryGetValue(Connection, out var cScope) &&
                cScope.RotationCenters != null &&
                cScope.RotationCenters.TryGetValue(Camera, out var rc))
            {
                CenterX = rc.X;
                CenterY = rc.Y;
                // rotationRmse 沒有存在 state 的話，就先設成 NaN 或 0
                CenterRmse = double.NaN;
            }
            else
            {
                CenterX = double.NaN;
                CenterY = double.NaN;
                CenterRmse = double.NaN;
            }

        }



        void ComputeOffset() => LastOffset = _svc.ComputeOffset(Connection, Camera);

        void SeedDemoData()
        {
            RobotPoints.Clear(); CameraPoints.Clear(); CameraToRealPoints.Clear(); LastOffset = new P3();

            double ang = 2.0 * Math.PI / 180.0; double c = Math.Cos(ang), s = Math.Sin(ang);
            double a = c, b = -s, tx = 100, cc = s, d = c, ty = 50;

            double[] xs = { 0, 50, 100 }; double[] ys = { 0, 50, 100 };
            foreach (var y in ys) foreach (var x in xs)
                {
                    var pix = new P3 { X = x, Y = y, U = 0 };
                    var real = new P3 { X = a * x + b * y + tx, Y = cc * x + d * y + ty, U = 0 };
                    CameraPoints.Add(pix); RobotPoints.Add(real);
                }

            _svc.RegisterGolden(Connection, Camera, new[] { new P3 { X = 0, Y = 0, U = 0 } }, new P3 { X = 100, Y = 50, U = 0 });

            Params.OffsetLimit.X = 5; Params.OffsetLimit.Y = 5; Params.OffsetLimit.U = 5;
            Params.OffsetTrim.X = 0; Params.OffsetTrim.Y = 0; Params.OffsetTrim.U = 0;

            CalibAndPreview();
            ComputeOffset();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        void OnPropertyChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        // 多相機清單（用來建欄位）
        public string[] MultiCalibCams { get; private set; }

        // 每一 row 表示一個 index（步驟）
        public ObservableCollection<MultiCamCalibRow> MultiCamCalibRows { get; }
            = new ObservableCollection<MultiCamCalibRow>();

        /// <summary>
        /// 初始化多相機校正表格（在開始 Multi Calib 前呼叫一次）
        /// </summary>
        public void InitMultiCalib(string[] cams, int expectedSteps)
        {
            MultiCalibCams = cams;

            MultiCamCalibRows.Clear();

            // 取得 CalibPosMatrix（相對位置）與 CalibMove（每格步距）
            var posList = _state.Const.CalibPosMatrix ?? new List<P3>();
            var step = Params.CalibMove ?? new P3();

            for (int i = 0; i < expectedSteps; i++)
            {
                MultiCamCalibRow row = new MultiCamCalibRow
                {
                    Index = i
                };

                // Calibrate 的 Real = CalibPosMatrix[i] * CalibMove
                if (i < posList.Count)
                {
                    var m = posList[i];
                    row.Real.X = m.X * step.X;
                    row.Real.Y = m.Y * step.Y;
                    row.Real.U = m.U * step.U;
                }
                // 如果 expectedSteps > CalibPosMatrix.Count，多出來的 row.Real 就維持 (0,0,0)

                MultiCamCalibRows.Add(row);
            }

            // 通知 View 重新產生欄位（下面會在 View 呼叫 BuildMultiCalibColumns）
            MultiCalibCamsChanged?.Invoke(this, EventArgs.Empty);
        }


        /// <summary>
        /// 更新某一步的所有相機 CCD 點（Flow 每一步 callback 時呼叫）
        /// </summary>
        public void ApplyMultiCalibStep(int stepIndex, Dictionary<string, P3> pixelsByCam)
        {
            if (stepIndex < 0 || stepIndex >= MultiCamCalibRows.Count)
                return;

            var row = MultiCamCalibRows[stepIndex];
            if (pixelsByCam == null)
                return;

            foreach (var kv in pixelsByCam)
            {
                row.SetCamPoint(kv.Key, kv.Value);
            }
        }

        // 提供給 View 重新建欄位用的事件（簡單就好）
        public event EventHandler MultiCalibCamsChanged;

    }

    static class ListExt
    {
        public static void AddRange<T>(this System.Collections.Generic.ICollection<T> dst, System.Collections.Generic.IEnumerable<T> src)
        { foreach (var x in src) dst.Add(x); }
    }
}
