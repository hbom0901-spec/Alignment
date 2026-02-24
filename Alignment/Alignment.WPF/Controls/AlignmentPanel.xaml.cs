using Alignment.WPF.ViewModels;
using System.ComponentModel;
using System.Windows.Controls;
using System;
using System.Windows;
using System.Windows.Data;
using Alignment.Core;

namespace Alignment.WPF.Controls
{
    public partial class AlignmentPanel : UserControl
    {
        public AlignmentPanel() 
        { 
            InitializeComponent();
            if (!DesignerProperties.GetIsInDesignMode(this))
                DataContext = new AlignmentPanelViewModel();
            DataContextChanged += AlignmentPanel_DataContextChanged;
        }
        private void AlignmentPanel_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is AlignmentPanelViewModel oldVm)
            {
                oldVm.MultiCalibCamsChanged -= Vm_MultiCalibCamsChanged;
            }

            if (e.NewValue is AlignmentPanelViewModel newVm)
            {
                newVm.MultiCalibCamsChanged += Vm_MultiCalibCamsChanged;
                BuildMultiCalibColumns(newVm);
            }
        }

        private void Vm_MultiCalibCamsChanged(object sender, EventArgs e)
        {
            if (sender is AlignmentPanelViewModel vm)
            {
                BuildMultiCalibColumns(vm);
            }
        }

        private void BuildMultiCalibColumns(AlignmentPanelViewModel vm)
        {
            if (vm.MultiCalibCams == null || vm.MultiCalibCams.Length == 0)
            {
                MultiCalibGrid.Columns.Clear();
                return;
            }

            MultiCalibGrid.Columns.Clear();

            // 第一欄：Index
            MultiCalibGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Index",
                Binding = new Binding("Index")
            });

            // 第二～四欄：Real.X / Real.Y / Real.U
            MultiCalibGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Real.X",
                Binding = new Binding("Real.X") { StringFormat = "F3" }
            });
            MultiCalibGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Real.Y",
                Binding = new Binding("Real.Y") { StringFormat = "F3" }
            });
            MultiCalibGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Real.U",
                Binding = new Binding("Real.U") { StringFormat = "F3" }
            });

            // 之後：每顆相機展開 X/Y/U 三欄
            foreach (var cam in vm.MultiCalibCams)
            {
                MultiCalibGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = $"{cam}.X",
                    Binding = new Binding($"CamPoints[{cam}].X") { StringFormat = "F3" }
                });
                MultiCalibGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = $"{cam}.Y",
                    Binding = new Binding($"CamPoints[{cam}].Y") { StringFormat = "F3" }
                });
                MultiCalibGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = $"{cam}.U",
                    Binding = new Binding($"CamPoints[{cam}].U") { StringFormat = "F3" }
                });
            }
        }

    }
}
