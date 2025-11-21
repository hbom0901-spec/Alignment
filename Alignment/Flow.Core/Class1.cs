// File: Alignment/Flow.Core/MainFlowController.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using Alignment.Core;
using Alignment.Coordinator.Core.Abstractions; // 引用 Coordinator 介面
using Core.Abstractions;
using System.Diagnostics;


namespace Flow.Core
{
    public class MainFlowController
    {
        private readonly IVisionService _vision;
        private readonly IMotionService _motion;
        private readonly IAlignmentCoordinator _coordinator; // 加入 Coordinator

        public MainFlowController(
            IVisionService vision,
            IMotionService motion,
            IAlignmentCoordinator coordinator)
        {
            _vision = vision;
            _motion = motion;
            _coordinator = coordinator;
        }

        public async Task InitializeSystemAsync()
        {
            await _motion.ConnectAsync();
            await _vision.ConnectAsync();

            // 設定 Coordinator 的 Vision Provider
            // 這裡我們用一個簡單的 Wrapper 把 IVisionService 包進去
            // (如果你的 Coordinator 已經改用 Core.Abstractions 就不需要 Wrapper，這裡假設需要轉接)
            _coordinator.SetVision(_vision);

            // 設定 Coordinator 的 Servo (Motion)
            // _coordinator.SetServo(...); // 如果 Coordinator 需要直接控制手臂可設，但我們這邊由 Flow 控制
        }

        // 這就是你要的「跟 Test 完全一樣」的流程
        public async Task RunCalibrationVerifyAsync(string conn, string cam, string jobId, Action<P3, P3> onStepFinished = null)
        {
            Console.WriteLine("=== Starting Calibration Flow (Similar to FlowTest) ===");

            // 1. 模擬移到初始位置 (Base Point)
            // 在你的測試中是 (1000, 2000)
            await _motion.MoveAbsoluteAsync(1000, 2000, 0);

            int measureCount = 12; // 12 點校正

            for (int i = 0; i < measureCount; i++)
            {
                // 2. 取得目前手臂位置
                var currentPos = await _motion.GetPositionAsync();

                // 3. 組裝指令 (CommandPacket)
                var cmd = new CommandPacket
                {
                    Conn = conn,
                    Cam = cam,
                    Command = AlignCommand.Calibrate,
                    JobId = jobId,
                    RobotX = currentPos.x,
                    RobotY = currentPos.y,
                    RobotU = currentPos.u
                };

                Debug.WriteLine($"Step {i + 1}/{measureCount}: Sending Calibrate Command...");

                // 4. 呼叫 Coordinator (它會叫相機拍照)
                var result = await _coordinator.HandleAsync(cmd);
                var robP3 = new P3 { X = currentPos.x, Y = currentPos.y, U = currentPos.u };
                var ccdP3 = result.Pixel; // Coordinator 回傳的 Pixel 點

                // 呼叫回調，把這兩個點丟給 UI
                onStepFinished?.Invoke(robP3, ccdP3);
                await Task.Delay(500);
                // 5. 檢查結果
                if (!result.Success)
                {
                    Debug.WriteLine($"Error: {result.Message}");
                    return;
                }

                Debug.WriteLine($"Coordinator Reply: Status={result.Status}, NextMove=({result.NextRobot.X:F2}, {result.NextRobot.Y:F2})");

                // 6. 依照指示移動手臂 (這就是 Test 中的 currentRobot += NextRobot)
                if (result.Status == 1) // 1 = Calibrating (還沒完，繼續走)
                {
                    await _motion.MoveRelativeAsync(result.NextRobot.X, result.NextRobot.Y, result.NextRobot.U);
                }
                else if (result.Status == 2) // 2 = Completed
                {
                    Debug.WriteLine($"Calibration Done! RMSE: {result.Rmse}");
                }
            }
        }
    }

    // 這是一個轉接頭，用來把 Core.Abstractions.IVisionService 
    // 轉成 Alignment.Coordinator 認得的 IVisionService (如果兩者介面定義不同)
    // 如果你已經統一介面，這個類別就不需要，直接傳 _vision 即可
    //public class VisionServiceAdapter : Alignment.Coordinator.Core.Abstractions.IVisionService
    //{
    //    private readonly IVisionService _realService;
    //    public VisionServiceAdapter(IVisionService svc) { _realService = svc; }

    //    public Task<P3> CaptureAsync(string cam, CancellationToken ct)
    //        => _realService.CaptureAsync(cam, ct);
    //}
}