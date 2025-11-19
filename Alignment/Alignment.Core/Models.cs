using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Alignment.Core
{
    public sealed class AlignmentConstants
    {
        public List<P3> CalibMoveMatrix { get; set; } = new List<P3> 
        {
            new P3 { X = -1, Y = 0, U = 0 },
            new P3 { X = 0, Y = -1, U = 0 },
            new P3 { X = 1, Y = 0, U = 0 },
            new P3 { X = 1, Y = 0, U = 0 },
           new P3 { X = 0, Y = 1, U = 0 },
            new P3 { X = 0, Y = 1, U = 0 },
            new P3 { X = -1, Y = 0, U = 0 },
            new P3 { X = 1, Y = -1, U = 0 },
            new P3 { X = 0, Y = 0, U = -1 },
            new P3 { X = 0, Y = 0, U = 2 },
            new P3 { X = 0, Y = 0, U = -1 },
            new P3 { X = 0, Y = 0, U = 0 }
        }; 
        public List<P3> CalibPosMatrix { get; set; } = new List<P3> 
        {
            new P3 { X = 0, Y = 0, U = 0 },
            new P3 { X = -1, Y = 0, U = 0 },
            new P3 { X = -1, Y = -1, U = 0 },
            new P3 { X = 0, Y = -1, U = 0 },
            new P3 { X = 1, Y = -1, U = 0 },
            new P3 { X = 1, Y = 0, U = 0 },
            new P3 { X = 1, Y = 1, U = 0 },
            new P3 { X = 0, Y = 1, U = 0 },
            new P3 { X = -1, Y = 1, U = 0 },
            new P3 { X = 0, Y = 0, U = 0 },
            new P3 { X = 0, Y = 0, U = -1 },
            new P3 { X = 0, Y = 0, U = 1 },
            new P3 { X = 0, Y = 0, U = 0 }
        }; 
    }

    public sealed class AlignmentParams : INotifyPropertyChanged
    {
        P3 _calibMove = new P3();
        P3 _offsetLimit = new P3();
        P3 _offsetTrim = new P3();

        AxisDir _xPositive = AxisDir.Right;
        AxisDir _yPositive = AxisDir.Down;
        RotDir _uRotation = RotDir.CCW;

        RCMethod _rotationMethod = RCMethod.AnglePair;

        public P3 CalibMove
        {
            get => _calibMove;
            set
            {
                if (ReferenceEquals(_calibMove, value)) return;
                _calibMove = value ?? new P3();
                OnPropertyChanged(nameof(CalibMove));
            }
        }

        public P3 OffsetLimit
        {
            get => _offsetLimit;
            set
            {
                if (ReferenceEquals(_offsetLimit, value)) return;
                _offsetLimit = value ?? new P3();
                OnPropertyChanged(nameof(OffsetLimit));
            }
        }

        public P3 OffsetTrim
        {
            get => _offsetTrim;
            set
            {
                if (ReferenceEquals(_offsetTrim, value)) return;
                _offsetTrim = value ?? new P3();
                OnPropertyChanged(nameof(OffsetTrim));
            }
        }

        public AxisDir XPositive
        {
            get => _xPositive;
            set
            {
                if (_xPositive == value) return;
                _xPositive = value;
                OnPropertyChanged(nameof(XPositive));
            }
        }

        public AxisDir YPositive
        {
            get => _yPositive;
            set
            {
                if (_yPositive == value) return;
                _yPositive = value;
                OnPropertyChanged(nameof(YPositive));
            }
        }

        public RotDir URotation
        {
            get => _uRotation;
            set
            {
                if (_uRotation == value) return;
                _uRotation = value;
                OnPropertyChanged(nameof(URotation));
            }
        }

        public RCMethod RotationMethod
        {
            get => _rotationMethod;
            set
            {
                if (_rotationMethod == value) return;
                _rotationMethod = value;
                OnPropertyChanged(nameof(RotationMethod));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        void OnPropertyChanged(string name)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed class CalibData
    {
        public P3 Origin { get; set; } = new P3();
        public Dictionary<string, VectorX> PixelToReal { get; } =
            new Dictionary<string, VectorX>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, VectorX> RealToPixel { get; } =
            new Dictionary<string, VectorX>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<P3>> PixelGolden { get; } =
            new Dictionary<string, List<P3>>(StringComparer.OrdinalIgnoreCase);
        public P3 RealGolden { get; set; } = new P3();
        public Dictionary<string, P3> RotationCenters { get; } =
            new Dictionary<string, P3>(StringComparer.OrdinalIgnoreCase);
    }
    public sealed class AlignmentState
    {
        public AlignmentConstants Const { get; } = new AlignmentConstants();
        public AlignmentParams Params { get; } = new AlignmentParams();

        public Dictionary<string, CalibData> ByConn { get; }
            = new Dictionary<string, CalibData>(StringComparer.OrdinalIgnoreCase);

        public List<P3> RobotPoints { get; } = new List<P3>();

        public Dictionary<string, List<P3>> CameraPoints { get; }
            = new Dictionary<string, List<P3>>(StringComparer.OrdinalIgnoreCase);
    }

}
