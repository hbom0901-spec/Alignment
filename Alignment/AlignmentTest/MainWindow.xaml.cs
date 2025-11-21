using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Alignment.Core;
using Alignment.Coordinator.Core.Internal;
using Alignment.WPF.Controls;
using Alignment.WPF.ViewModels;
using Core.Abstractions;
using Device.Simulation;
using Flow.Core;
using Alignment.Coordinator.Core.Abstractions;
using System.Diagnostics;
using Newtonsoft.Json;

namespace AlignmentTest
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly AlignmentState _state;
        private readonly IAlignmentConfig _cfg;
        private readonly IAlignmentService _svc;
        private readonly AlignmentCoordinator _coord;
        private readonly AlignmentPanelViewModel _vm;

        private MainFlowController _flowController;
        public MainWindow()
        {
            InitializeComponent();

            // ==================================================
            // 1. 建立模擬硬體 (Simulation Layer)
            // ==================================================
            // 先建立 Motion，因為 Vision 需要知道手臂位置
            IMotionService motion = new SimulatedMotionService();

            // 建立 Vision，並注入 Motion (這樣相機才能根據手臂位置算出 Pixel)
            IVisionService simvision = new SimulatedVisionService(motion);

            // ==================================================
            // 2. 建立核心邏輯與設定 (Domain Layer)
            // ==================================================
            _state = new AlignmentState();

            // 使用 JSON Config，確保參數能存檔
            _cfg = new JsonAlignmentConfig();

            // 如果您希望一開始有預設值，可以手動存一次(僅測試用)
            // EnsureDefaultParams(_cfg); 

            _svc = new AlignmentService(_state, _cfg);

            // 建立 Coordinator (協調者)
            _coord = new AlignmentCoordinator(_svc);

            // ★★★ 關鍵修正 ★★★
            // 必須在這裡設定 Config，否則 Coordinator 不知道校正要跑哪幾個點
            // 這就是導致 "ArgumentOutOfRangeException" 的原因
            _coord.SetConfig(_cfg);

            // 設定 Logger (選用)
            _coord.SetLogger(new DebugLogger());

            // 將模擬相機設定給 Coordinator
            // 這裡使用 Adapter 或者是統一介面後的直接注入
            // 假設您已經按照建議統一了 Core.Abstractions.IVisionService
            _coord.SetVision(simvision);

            // ==================================================
            // 3. 建立主流程控制器 (App Layer)
            // ==================================================
            // 注入剛剛建立好的 vision, motion, 和 "已經設定好的" coordinator
            _flowController = new MainFlowController(simvision, motion, _coord);

            // ==================================================
            // 4. 建立 UI ViewModel (Presentation Layer)
            // ==================================================
            _vm = new AlignmentPanelViewModel(_svc, _cfg, _state);

            // 設定 UI 預設值，方便測試
            _vm.Connection = "CONN1";
            _vm.Camera = "CCD1";

            AlignmentPanelControl.DataContext = _vm;

            // 1) Calib 常數與參數
            AlignmentConstants consts = new AlignmentConstants();
            AlignmentParams parms = new AlignmentParams
            {
                CalibMove = new P3 { X = 110, Y = 110, U = 5 }
            };
            // 2) 共用 AlignmentState + Config + Service
            _state = new AlignmentState();
            _cfg = new JsonAlignmentConfig();
            //_cfg = new MemoryAlignmentConfig(parms, consts);
            _svc = new AlignmentService(_state, _cfg);

            // 3) Coordinator 吃同一個 svc
            _coord = new AlignmentCoordinator(_svc);
            _coord.SetConfig(_cfg);
            _coord.SetLogger(new DebugLogger());

            // 4) 假 CCD 資料，移出 FlowTest，統一放這裡
            var ccdSeq = new List<P3>
            {
                new P3 { X=1854.916, Y=1577.918 },
                new P3 { X=2724.167, Y=1574.615 },
                new P3 { X=2720.464, Y=709.261 },
                new P3 { X=1852.647, Y=717.672 },
                new P3 { X=986.073,  Y=721.674 },
                new P3 { X=989.928,  Y=1584.328 },
                new P3 { X=995.685,  Y=2445.826 },
                new P3 { X=1860.891, Y=2441.617 },
                new P3 { X=2725.389, Y=2437.412 },
                new P3 { X=1851.717, Y=1576.801 },
                new P3 { X=1829.247, Y=1585.974 },
                new P3 { X=1872.619, Y=1559.839 },
                new P3 { X=1854.916, Y=1577.918 }
            };

            var camName = "CCD1";
            var vision = new FakeVisionService(ccdSeq);
            var vp = new FakeVisionProvider(camName, vision);
            _coord.SetVisionProvider(vp);

            // 5) WPF AlignmentPanel 綁定到同一份 _state/_svc
            _vm = new AlignmentPanelViewModel(_svc, _cfg, _state);
            AlignmentPanelControl.DataContext = _vm;
        }
        private async void BtnRunCalibrateTest_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _vm.Connection = "CONN1";
                await CalibrateFlowTest.RunAsync(_coord);
                _vm.ReloadFromState(_coord.State);
                MessageBox.Show("CalibrateFlowTest 已執行完成");
            }
            catch (Exception ex)
            {
                MessageBox.Show("測試失敗：" + ex.Message);
            }
        }
        // 按鈕事件：測試 FlowController (新的架構)
        // 檔案：AlignmentTest/MainWindow.xaml.cs

        private async void BtnRun_Click(object sender, RoutedEventArgs e)
        {
            BtnRun.IsEnabled = false;

            // 執行前先把舊資料清空，這樣畫面上才會從零開始長出來
            _state.RobotPoints.Clear();
            _state.CameraPoints.Clear();
            _vm.ReloadFromState();

            try
            {
                await _flowController.InitializeSystemAsync();

                // 這裡的 lambda 變成了接收兩個參數 (rob, ccd)
                await _flowController.RunCalibrationVerifyAsync(
                    _vm.Connection,
                    _vm.Camera,
                    "JOB_TEST_001",
                    (rob, ccd) =>
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                    // 1. 把 Flow 吐出來的資料塞進 State
                    _state.RobotPoints.Add(rob);

                            if (!_state.CameraPoints.ContainsKey(_vm.Camera))
                            {
                                _state.CameraPoints[_vm.Camera] = new List<P3>();
                            }
                            _state.CameraPoints[_vm.Camera].Add(ccd);

                    // 2. 現在 State 有資料了，叫 VM 重新讀取，圖表就會動了！
                    _vm.ReloadFromState();
                        });
                    }
                );

                MessageBox.Show("流程執行結束");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"執行失敗: {ex.Message}");
            }
            finally
            {
                BtnRun.IsEnabled = true;
            }
        }

        // 按鈕事件：初始化 (單獨)
        private async void BtnInit_Click(object sender, RoutedEventArgs e)
        {
            BtnInit.IsEnabled = false;
            await _flowController.InitializeSystemAsync();
            BtnInit.IsEnabled = true;
            MessageBox.Show("系統初始化完成 (模擬模式)");
        }
        private void EnsureDefaultParams(IAlignmentConfig cfg)
        {
            var p = cfg.LoadParams();
            if (p.CalibMove.X == 0 && p.CalibMove.Y == 0)
            {
                p.CalibMove = new P3 { X = 100, Y = 100, U = 5 };
                cfg.SaveParams(p);
            }
        }
        sealed class DebugLogger : ISystemLogger
        {
            public void Info(string tag, object data = null)
            {
                Debug.WriteLine($"INFO [{tag}]: {JsonConvert.SerializeObject(data)}");
            }

            public void Warn(string tag, object data = null)
            {
                Debug.WriteLine($"WARN [{tag}]: {JsonConvert.SerializeObject(data)}");
            }

            public void Error(string tag, object data = null)
            {
                if (data is Exception ex)
                    Debug.WriteLine($"ERR  [{tag}]: {ex.Message}");
                else
                    Debug.WriteLine($"ERR  [{tag}]: {JsonConvert.SerializeObject(data)}");
            }
        }
    }
}
