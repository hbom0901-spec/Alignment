using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Alignment.Coordinator.Core.Abstractions
{
    public interface IAlignmentCoordinator
    {
        Alignment.Core.AlignmentState State { get; }

        void SetVision(IVisionService vision);
        void SetVisionProvider(IVisionProvider provider);
        void SetServo(IServoController servo);
        void SetLogger(ISystemLogger logger);
        void SetConfig(Alignment.Core.IAlignmentConfig config);

        Task<CommandResult> HandleAsync(CommandPacket cmd, CancellationToken ct = default);
        // 其他工具 API 可視需要加
    }
}