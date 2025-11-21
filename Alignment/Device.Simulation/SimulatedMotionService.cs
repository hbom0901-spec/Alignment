using System;
using System.Threading.Tasks;
using System.Diagnostics;
using Core.Abstractions; // 記得引用你的合約層

namespace Device.Simulation
{
    public class SimulatedMotionService : IMotionService
    {
        // 模擬內部狀態：目前手臂的位置
        private double _currentX = 0.0;
        private double _currentY = 0.0;
        private double _currentU = 0.0;

        // 模擬連線狀態
        private bool _isConnected = false;

        public Task<bool> ConnectAsync()
        {
            _isConnected = true;
            Debug.WriteLine("[SimMotion] Robot Connected (Virtual).");
            return Task.FromResult(true);
        }

        public Task DisconnectAsync()
        {
            _isConnected = false;
            Debug.WriteLine("[SimMotion] Robot Disconnected.");
            return Task.CompletedTask;
        }

        // 場景 A：絕對移動 (直接指定去哪裡)
        // 例如：回到原點、移動到拍照位
        public async Task MoveAbsoluteAsync(double x, double y, double u)
        {
            if (!_isConnected) throw new Exception("Robot not connected!");

            Debug.WriteLine($"[SimMotion] ABS Move -> Target: ({x:F3}, {y:F3}, {u:F3})...");

            // 模擬移動耗時 (假設絕對移動比較遠，跑 1 秒)
            await Task.Delay(1000);

            _currentX = x;
            _currentY = y;
            _currentU = u;

            Debug.WriteLine($"[SimMotion] Arrived at ({_currentX:F3}, {_currentY:F3}, {_currentU:F3})");
        }

        // 場景 B：相對移動 / 補正 (告訴手臂要偏多少)
        // 例如：視覺算出來差 0.05mm，就只移 0.05mm
        public async Task MoveRelativeAsync(double dx, double dy, double du)
        {
            if (!_isConnected) throw new Exception("Robot not connected!");

            Debug.WriteLine($"[SimMotion] REL Move -> Offset: ({dx:F3}, {dy:F3}, {du:F3}) from Current: ({_currentX:F3}, {_currentY:F3}, {_currentU:F3})");

            // 模擬移動耗時 (補正通常距離短，跑 0.5 秒)
            await Task.Delay(500);

            // ★★★ 關鍵差異：這裡是「累加」 ★★★
            _currentX += dx;
            _currentY += dy;
            _currentU += du;

            Debug.WriteLine($"[SimMotion] Shifted to New Pos: ({_currentX:F3}, {_currentY:F3}, {_currentU:F3})");
        }

        public Task<(double x, double y, double u)> GetPositionAsync()
        {
            // 即使沒連線，有些驅動器也能讀最後位置，這裡假設連線才能讀
            if (!_isConnected)
                Debug.WriteLine("[SimMotion] Warning: Reading position while disconnected.");

            return Task.FromResult((_currentX, _currentY, _currentU));
        }

        public async Task HomeAsync()
        {
            Debug.WriteLine("[SimMotion] Homing...");
            await Task.Delay(2000);
            _currentX = 0; _currentY = 0; _currentU = 0;
        }
    }
}