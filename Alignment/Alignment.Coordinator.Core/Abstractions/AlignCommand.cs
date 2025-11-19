using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Alignment.Coordinator.Core.Abstractions
{
    public enum AlignCommand
    {
        Calibrate = 0,
        Register = 1,
        Align = 2,
        Reset = 7
    }
}
