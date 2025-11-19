using System;
using System.ComponentModel;

namespace Alignment.Core
{
    // UI 需要雙向繫結，點用 class + INotifyPropertyChanged
    public sealed class P3 : INotifyPropertyChanged
    {
        double _x, _y, _u;
        public double X { get => _x; set { if (_x == value) return; _x = value; OnPropertyChanged(nameof(X)); } }
        public double Y { get => _y; set { if (_y == value) return; _y = value; OnPropertyChanged(nameof(Y)); } }
        public double U { get => _u; set { if (_u == value) return; _u = value; OnPropertyChanged(nameof(U)); } }
        public P3 Clone() => new P3 { X = X, Y = Y, U = U };
        public event PropertyChangedEventHandler PropertyChanged;
        void OnPropertyChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
    public struct VectorX
    {
        public double a, b, c, d, e, f; // X = a x + b y + c;  Y = d x + e y + f
    }
    public struct CalibInfo { public double  ThetaDeg, Sx, Sy, Shear, sThetaDeg, sSx, sSy; }
    public enum RCMethod
    {
        CircleFit = 0,   // 使用點集擬合圓心（不依賴角度）
        AnglePair = 1    // 使用點對 + U(角度) 來求中心（原 WithAngles）
    }
}
