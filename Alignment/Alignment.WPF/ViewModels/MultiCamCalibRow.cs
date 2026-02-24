using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Alignment.Core;

public sealed class MultiCamCalibRow : INotifyPropertyChanged
{
    public int Index { get; set; }

    /// <summary>
    /// Calibrate 時要顯示的「Real」座標：
    /// = AlignmentConstants.CalibPosMatrix[i] * AlignmentParams.CalibMove
    /// </summary>
    public P3 Real { get; } = new P3();

    // key = camera name, value = CCD point at this index
    public Dictionary<string, P3> CamPoints { get; }
        = new Dictionary<string, P3>();

    public event PropertyChangedEventHandler PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// 設定某一顆相機在此 index 的 CCD 點，並通知 UI 更新。
    /// </summary>
    public void SetCamPoint(string cam, P3 value)
    {
        CamPoints[cam] = value;
        // 通知整個 CamPoints 變了，Binding "CamPoints[cam].X" 會重新取值
        OnPropertyChanged(nameof(CamPoints));
    }
}
