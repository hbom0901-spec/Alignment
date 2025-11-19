using System.Collections.Concurrent;
using Alignment.Core;

namespace Alignment.Coordinator.Core.State
{
    internal sealed class CoordinatorState
    {
        // 最終校正資料（矩陣/Golden/旋心）
       // public AlignmentState Calibs { get; } = new AlignmentState();

        // 每連線額外快取
        public ConcurrentDictionary<string, P3> LastRobotPointByConn { get; }
            = new ConcurrentDictionary<string, P3>();

        // 在途暫存
        public ConcurrentDictionary<(string conn, string cam), CalibPlan> CalibPlans
            = new ConcurrentDictionary<(string, string), CalibPlan>();

        public ConcurrentDictionary<(string conn, string jobId), RegisterPlan> RegisterPlans
            = new ConcurrentDictionary<(string, string), RegisterPlan>();

        public ConcurrentDictionary<(string conn, string jobId), AlignJob> AlignJobs
            = new ConcurrentDictionary<(string, string), AlignJob>();
    }
}
