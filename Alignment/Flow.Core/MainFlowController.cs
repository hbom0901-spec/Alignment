// File: Alignment/Flow.Core/MainFlowController.cs
using System;
using System.Threading.Tasks;
using Alignment.Core;
using Alignment.Coordinator.Core.Abstractions; // 引用 Coordinator 介面
using Core.Abstractions;
using System.Diagnostics;
using System.Collections.Generic;

namespace Flow.Core
{
    public class MainFlowController
    {
        private readonly IVisionService _vision;
        private readonly IMotionService _motion;
        private readonly IAlignmentCoordinator _coordinator; // Coordinator

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
            _coordinator.SetVision(_vision);

            // 如果未來需要讓 Coordinator 直接控制 Servo，可以在這裡 SetServo
            // _coordinator.SetServo(...);
        }

        /// <summary>
        /// 單相機校正流程（原本就有的版本，完全不改）
        /// </summary>
        public async Task RunCalibrationVerifyAsync(
            string conn,
            string cam,
            string jobId,
            Action<P3, P3> onStepFinished = null)
        {
            Console.WriteLine("=== Starting Calibration Flow (Single Cam, Similar to FlowTest) ===");

            // 1. 移到初始位置 (Base Point)
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
                    Cams = null,              // 單相機模式
                    Command = AlignCommand.Calibrate,
                    JobId = jobId,
                    RobotX = currentPos.x,
                    RobotY = currentPos.y,
                    RobotU = currentPos.u
                };

                Debug.WriteLine($"[SingleCam] Step {i + 1}/{measureCount}: Sending Calibrate Command...");

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
                    Debug.WriteLine($"[SingleCam] Error: {result.Message}");
                    return;
                }

                Debug.WriteLine(
                    $"[SingleCam] Reply: Status={result.Status}, NextMove=({result.NextRobot.X:F2}, {result.NextRobot.Y:F2}, {result.NextRobot.U:F2})");

                // 6. 依照指示移動手臂 (這就是 Test 中的 currentRobot += NextRobot)
                if (result.Status == 1) // 1 = Calibrating (還沒完，繼續走)
                {
                    await _motion.MoveRelativeAsync(result.NextRobot.X,
                                                    result.NextRobot.Y,
                                                    result.NextRobot.U);
                }
                else if (result.Status == 2) // 2 = Completed
                {
                    Debug.WriteLine($"[SingleCam] Calibration Done! RMSE: {result.Rmse}");
                    return;
                }
            }
        }

        /// <summary>
        /// 多相機校正流程：
        /// 同一條 Robot 路徑，同步對多顆相機做校正（一次 Calibrate 處理多個 Cams）。
        /// </summary>
        public async Task RunCalibrationMultiAsync(
            string conn,
            string[] cams,
            string jobId,
            Action<Dictionary<string, P3>, P3> onStepFinished = null)
        {
            if (cams == null || cams.Length == 0)
                throw new ArgumentException("cams must contain at least one camera.", nameof(cams));

            Console.WriteLine("=== Starting Calibration Flow (Multi Cam) ===");
            Console.WriteLine($"Cams: {string.Join(", ", cams)}");

            // 1. 移到初始位置 (Base Point) – 依你現在線上實機需求調整
            await _motion.MoveAbsoluteAsync(1000, 2000, 0);

            while (true)
            {
                // 2. 取得目前手臂位置
                var currentPos = await _motion.GetPositionAsync();

                // 3. 組裝多相機指令 (CommandPacket)
                var cmd = new CommandPacket
                {
                    Conn = conn,
                    Cam = null,
                    Cams = cams,
                    Command = AlignCommand.Calibrate,
                    JobId = jobId,
                    RobotX = currentPos.x,
                    RobotY = currentPos.y,
                    RobotU = currentPos.u
                };

                var result = await _coordinator.HandleAsync(cmd);

                if (!result.Success)
                {
                    Debug.WriteLine($"[MultiCam] Error: {result.Message}");
                    return;
                }

                // 4. 把所有相機的 CCD 點丟給 VM
                Dictionary<string, P3> pixelsByCam = result.PixelsByCam;

                // 保險：若 PixelsByCam 為 null（例如單相機或完成時），就建立一份只含主鏡頭的 map
                if (pixelsByCam == null)
                {
                    pixelsByCam = new Dictionary<string, P3>();
                    // 取第一顆當 primary
                    if (cams.Length > 0)
                        pixelsByCam[cams[0]] = result.Pixel;
                }

                var realP3 = new P3 { X = currentPos.x, Y = currentPos.y, U = currentPos.u };
                onStepFinished?.Invoke(pixelsByCam, realP3);

                Debug.WriteLine(
                    $"[MultiCam] Reply: Status={result.Status}, NextMove=({result.NextRobot.X:F2}, {result.NextRobot.Y:F2}, {result.NextRobot.U:F2})");

                // 5. 根據 Status 決定是否繼續走
                if (result.Status == 1)
                {
                    await _motion.MoveRelativeAsync(result.NextRobot.X,
                                                    result.NextRobot.Y,
                                                    result.NextRobot.U);
                    continue;
                }
                else if (result.Status == 2)
                {
                    Debug.WriteLine(
                        $"[MultiCam] Calibration Done! RMSE: {result.Rmse}, Completed={result.Completed}/{result.Required}");
                    return;
                }
                else
                {
                    Debug.WriteLine($"[MultiCam] Unexpected Status: {result.Status}");
                    return;
                }
            }
        }
    }

        // 如果你之後有需要轉接不同的 IVisionService 介面，
        // 可以再把你註解掉的 Adapter 拿回來用；
        // 目前看起來 IAlignmentCoordinator 已經吃的是 Core.Abstractions.IVisionService，就不需要 Adapter。
    }
