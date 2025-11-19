using Alignment.WPF.ViewModels;
using System.ComponentModel;
using System.Windows.Controls;

namespace Alignment.WPF.Controls
{
    public partial class AlignmentPanel : UserControl
    {
        public AlignmentPanel() 
        { 
            InitializeComponent();
            if (!DesignerProperties.GetIsInDesignMode(this))
                DataContext = new AlignmentPanelViewModel();
        }
    }
}
