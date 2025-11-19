using System;
using System.Globalization;
using System.Windows.Data;
using Alignment.Core;

namespace Alignment.WPF.Utils
{
    public sealed class AxisDirToGlyphConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            switch ((AxisDir)value)
            {
                case AxisDir.Right: return "→";
                case AxisDir.Left: return "←";
                case AxisDir.Up: return "↑";
                case AxisDir.Down: return "↓";
            }
            return "?";
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
    }
}
