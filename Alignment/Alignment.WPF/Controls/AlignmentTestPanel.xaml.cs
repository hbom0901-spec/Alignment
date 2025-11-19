using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Alignment.Core;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Collections.ObjectModel;
using Alignment.WPF.ViewModels;

namespace Alignment.WPF.Controls
{
    public partial class AlignmentTestPanel : UserControl
    {
        public AlignmentTestPanel()
        {
            InitializeComponent();
            DataContext = this;
            RobotPoints.AddRange(new[]
            {
                new P3{X=0,Y=0},
                new P3{X=-110,Y=0},
                new P3{X=-110,Y=-110},
                new P3{X=0,Y=-110},
                new P3{X=110,Y=-110},
                new P3{X=110,Y=0},
                new P3{X=110,Y=110},
                new P3{X=0,Y=110},
                new P3{X=-110,Y=110}
            });

            PixelPoints.AddRange(new[]
            {
                new P3{X=1854.916,Y=1577.918},
                new P3{X=2724.167,Y=1574.615},
                new P3{X=2720.464,Y=709.261},
                new P3{X=1852.647,Y=717.672},
                new P3{X=986.073,Y=721.674},
                new P3{X=989.928,Y=1584.328},
                new P3{X=995.685,Y=2445.826},
                new P3{X=1860.891,Y=2441.617},
                new P3{X=2725.389,Y=2437.412}
            });

            RotateCenterPoints.AddRange(new[]
                {
                new P3 { X = 1851.717, Y = 1576.801},
                new P3 { X = 1829.247, Y = 1585.974},
                new P3 { X = 1872.619, Y = 1559.839}
                });

        }

        public ObservableCollection<P3> RobotPoints { get; } = new ObservableCollection<P3>();
        public ObservableCollection<P3> PixelPoints { get; } = new ObservableCollection<P3>();
        public ObservableCollection<P3> RotateCenterPoints { get; } = new ObservableCollection<P3>();

        private void Log(string msg)
        {
            txtOutput.AppendText(msg + Environment.NewLine);
            txtOutput.ScrollToEnd();
        }

        private void OnRunAffine(object sender, RoutedEventArgs e)
        {
            txtOutput.Clear();
            try
            {
                if (RobotPoints.Count < 3 || PixelPoints.Count < 3)
                {
                    Log("Error: Need ≥3 point pairs.");
                    return;
                }

                VectorX theta = AffineLinearSolver.SolveVectorX(PixelPoints, RobotPoints);
                CalibInfo info = AffineDecompose.GetCalibInfo(theta);

                Log("=== Affine Calibration ===");
                Log($"a = {theta.a:F6}");
                Log($"b = {theta.b:F6}");
                Log($"c = {theta.c:F3}");
                Log($"d = {theta.d:F6}");
                Log($"e = {theta.e:F6}");
                Log($"f = {theta.f:F3}");
                Log($"θ  = {info.ThetaDeg:F3}°");
                Log($"Sx = {info.Sx:F6}");
                Log($"Sy = {info.Sy:F6}");
            }
            catch (Exception ex)
            {
                Log("Error: " + ex.Message);
            }
        }

        private void OnRunCenter(object sender, RoutedEventArgs e)
        {
            txtOutput.Clear();
            try
            {
                var list = RotateCenterPoints?.ToList();
                if (list == null || list.Count < 3)
                {
                    Log("Error: need 3 points in RotateCenter Data");
                    return;
                }
                List<P3> realangle = new List<P3>
                {
                    new P3 { U =0},
                    new P3 { U =5},
                    new P3 { U =-5}
                };
                if (chkUseAngle.IsChecked == true)
                {
                    (P3 center, double rmse) = RotationCenter.CalculateRotateCenter( list, realangle );
                    Log("=== Rotation Center (Angle Reverse Solve) ===");
                    Log($"Center=({center.X:F3},{center.Y:F3}), RMSE={rmse:F6}");
                }
                else
                {
                    // 圓擬合法：可用>3點，這裡就用整個 RotateCenterPoints
                    (P3 center,  double rmse) = RotationCenter.CalculateByFitKasa(list);
                    Log("=== Rotation Center (Circle Fit) ===");
                    Log($"Center=({center.X:F3},{center.Y:F3}), RMSE={rmse:F6}");
                }
            }
            catch (Exception ex)
            {
                Log("Error: " + ex.Message);
            }
        }

        void OnImportRaw(object sender, RoutedEventArgs e)
        {
            var text = txtRaw.Text;
            if (string.IsNullOrWhiteSpace(text)) { Log("No input."); return; }

            // 取區段
            string rotateSec = ExtractSection(text, "Rotate raw data", "Calib raw data");
            string calibSec = ExtractSection(text, "Calib raw data", null);

            // 解析
            var rotatePts = ParsePoints(rotateSec);
            var calibPts = ParsePoints(calibSec);

            if (rotatePts.Count == 0) { Log("Rotate raw data not found."); return; }
            if (calibPts.Count < 3) { Log("Calib raw data need ≥3 lines."); return; }

            // 寫入集合
            PixelPoints.Clear();
            foreach (var p in rotatePts) PixelPoints.Add(p);

            RotateCenterPoints.Clear();
            foreach (var p in calibPts.Take(3)) RotateCenterPoints.Add(p);

            // 若你未使用 DataContext 綁定，請確保三個 DataGrid 都有 ItemsSource 指定
            // dgPixel.ItemsSource = PixelPoints;
            // dgRotateCenter.ItemsSource = RotateCenterPoints;

            Log($"Imported: Pixel={PixelPoints.Count}, RotateCenter={RotateCenterPoints.Count}");
        }

        // 解析一段 "Pxx: x,y,u" 列表
        static List<P3> ParsePoints(string section)
        {
            var list = new List<P3>();
            if (string.IsNullOrWhiteSpace(section)) return list;

            var rx = new Regex(@"P\d+:\s*([+-]?\d+(?:\.\d+)?)\s*,\s*([+-]?\d+(?:\.\d+)?)\s*,\s*([+-]?\d+(?:\.\d+)?)",
                               RegexOptions.IgnoreCase);
            foreach (Match m in rx.Matches(section))
            {
                double x = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                double y = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                double u = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
                list.Add(new P3 { X = x, Y = y, U = u });
            }
            return list;
        }

        // 擷取兩個標題之間的文字；endHeader=null 代表到結尾
        static string ExtractSection(string text, string startHeader, string endHeader)
        {
            int si = text.IndexOf(startHeader, StringComparison.OrdinalIgnoreCase);
            if (si < 0) return "";
            si += startHeader.Length;
            int ei = endHeader == null ? text.Length
                     : text.IndexOf(endHeader, si, StringComparison.OrdinalIgnoreCase);
            if (ei < 0) ei = text.Length;
            return text.Substring(si, ei - si);
        }
    }
}
