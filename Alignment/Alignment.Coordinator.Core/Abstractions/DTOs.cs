using System.Collections.Generic;
using Alignment.Core;

namespace Alignment.Coordinator.Core.Abstractions
{
    public sealed class CommandPacket
    {
        public string Conn;     // 連線名稱
        public string Cam;      // 相機名稱（多相機任務時可忽略，由 Module 另行處理）
        public AlignCommand Command;
        public string JobId;    // 任務識別（可由 PLC 或 Guid）
        public double RobotX, RobotY, RobotU; // 當次 robot 點（快照）
        public IList<string> Cams; // 多相機名單（Register/Align 用）
    }

    public struct CommandResult
    {
        public bool Success;
        public int Status;     // 0=OK, 1=進行中, 2=完成, -1=失敗
        public string Message;
        public string JobId;

        public int Completed;  // 校正已完成點數
        public int Required;   // 校正預期點數

        public P3 Pixel;       // 最近像素
        public P3 Real;        // 最近實座標
        public P3 Offset;      // Real - RealGolden
        public double Rmse;    // 校正/對位估計誤差

        // 校正中回覆給外部的「下一步移動量」（CalibMoveMatrix * CalibMove 的其中一段）
        public P3 NextRobot;
    }

    public struct CalibResult
    {
        public string Cam;
        public double Rmse;
        public VectorX PixelToReal;
        public VectorX RealToPixel;
        public double RotDeg;
        public double Sx;
        public double Sy;
        public double Shear;
        public int PairCount;
    }

    public struct RCResult
    {
        public string Cam;
        public P3 Center;
        public double Radius;
        public double Rmse;
    }

    public enum ResetScope
    {
        ProgressOnly,
        AllCalibration
    }
}
