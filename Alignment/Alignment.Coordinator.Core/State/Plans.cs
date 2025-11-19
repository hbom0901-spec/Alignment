using System;
using System.Collections.Generic;
using Alignment.Core;

namespace Alignment.Coordinator.Core.State
{
    // 多點校正計畫（每 conn/cam 一份）
    internal sealed class CalibPlan
    {
        public string JobId = Guid.NewGuid().ToString("N");
        public int Version;                   // Reset 後 +1
        public int RequiredCount;
        public readonly List<(P3 ccd, P3 rob)> Pairs = new List<(P3, P3)>();
        public bool IsDone;
        public int CompletedCount => Pairs.Count;

        // 第一次 Calibrate 時記錄的 BaseRobot（給你之後 Align 用來檢查 RC 用）
        public bool BaseRobotSet;
        public P3 BaseRobot;

        // 理論 pattern：CalibPosMatrix * CalibMove → RobotIdeal[i]
        public List<P3> PosList;

        // 移動量：CalibMoveMatrix * CalibMove → Steps[i]（回給外部做下一步移動量）
        public List<P3> Steps;
    }

    // 基準登錄聚合（一次任務，可多相機）
    internal sealed class RegisterPlan
    {
        public string JobId = Guid.NewGuid().ToString("N");
        public HashSet<string> ExpectedCams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, P3> Pixels = new Dictionary<string, P3>(StringComparer.OrdinalIgnoreCase);
        public P3 RealGoldenSnapshot;
        public DateTime DeadlineUtc;
    }

    // 對位任務（一次任務，可多相機）
    internal sealed class AlignJob
    {
        public string JobId = Guid.NewGuid().ToString("N");
        public HashSet<string> ExpectedCams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, P3> Pixels = new Dictionary<string, P3>(StringComparer.OrdinalIgnoreCase);
        public P3 RobotAtTrigger;
        public DateTime DeadlineUtc;
    }
}
