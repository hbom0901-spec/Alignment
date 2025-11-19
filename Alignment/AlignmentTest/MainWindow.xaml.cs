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
        public MainWindow()
        {
            InitializeComponent();

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
    }
}
