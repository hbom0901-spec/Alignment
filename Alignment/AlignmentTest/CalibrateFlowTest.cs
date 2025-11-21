using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Alignment.Core;
using Newtonsoft.Json;
using Alignment.Coordinator.Core.Abstractions;
using Alignment.Coordinator.Core.Internal;
using Core.Abstractions;

namespace AlignmentTest
{
    // 1) 簡單的 in-memory Config，不碰硬碟
    sealed class MemoryAlignmentConfig : IAlignmentConfig
    {
        private readonly AlignmentParams _params;
        private readonly AlignmentConstants _consts;

        public MemoryAlignmentConfig(AlignmentParams p, AlignmentConstants c)
        {
            _params = p;
            _consts = c;
        }

        public void SaveParams(AlignmentParams p) { /* 測試用，不做事 */ }
        public AlignmentParams LoadParams() => _params;

        public void SaveConstants(AlignmentConstants c) { /* 測試用，不做事 */ }
        public AlignmentConstants LoadConstants() => _consts;
    }

    // 2) 假的 Camera / VisionProvider：依照呼叫次數回 CCD 點
    sealed class FakeVisionService : IVisionService
    {
        private readonly List<P3> _ccdSeq;
        private int _index;

        public FakeVisionService(List<P3> ccdSeq)
        {
            _ccdSeq = ccdSeq;
            _index = 0;
        }

        public Task<P3> CaptureAsync(string cam, CancellationToken ct)
        {
            var i = _index < _ccdSeq.Count ? _index : _ccdSeq.Count - 1;
            var p = _ccdSeq[i];
            _index++;
            return Task.FromResult(p);
        }

        public Task<bool> ConnectAsync()
        {
            throw new NotImplementedException();
        }

        public Task DisconnectAsync()
        {
            throw new NotImplementedException();
        }
    }


    sealed class FakeVisionProvider : IVisionProvider
    {
        private readonly Dictionary<string, IVisionService> _cams =
            new Dictionary<string, IVisionService>(StringComparer.OrdinalIgnoreCase);

        public FakeVisionProvider(string camName, IVisionService svc)
        {
            _cams[camName] = svc;
        }

        public IVisionService Get(string cam)
        {
            return _cams[cam];
        }
    }


    // 3) 簡單 logger


    // 4) 實際測試流程
    static class CalibrateFlowTest
    {
        public static async Task RunAsync(AlignmentCoordinator coord, CancellationToken ct = default)
        {
            if (coord == null) throw new ArgumentNullException(nameof(coord));

            var conn = "CONN1";
            var cam = "CCD1";
            var jobId = "JOB_CALIB_TEST";

            int measureCount = 12;
            // BaseRobot：實機中心點
            P3 currentRobot = new P3 { X = 1000, Y = 2000, U = 0 };

            CommandResult lastResult = default;

            // ==== 只發 12 次 Calibrate（對應 1~12 點）====
            for (int i = 0; i < measureCount; i++)
            {
                CommandPacket cmd = new CommandPacket
                {
                    Conn = conn,
                    Cam = cam,
                    Command = AlignCommand.Calibrate,
                    JobId = jobId,
                    RobotX = currentRobot.X,
                    RobotY = currentRobot.Y,
                    RobotU = currentRobot.U
                };

                CommandResult r = await coord.HandleAsync(cmd, ct).ConfigureAwait(false);
                lastResult = r;

                Debug.WriteLine(
                    $"Step {i + 1}/{measureCount}: Status={r.Status}, Completed={r.Completed}, Required={r.Required}, NextMove=({r.NextRobot.X},{r.NextRobot.Y},{r.NextRobot.U})");

                // 下一輪 Robot = 目前位置 + NextMove（把 NextRobot 當作移動量）
                currentRobot = new P3
                {
                    X = currentRobot.X + r.NextRobot.X,
                    Y = currentRobot.Y + r.NextRobot.Y,
                    U = currentRobot.U + r.NextRobot.U
                };
            }

            // 這裡你可以額外模擬第 13 點「走回中心」的 Move，但不送 Calibrate 指令
            // currentRobot = baseCenter; // 之類的，純粹動作

            // ==== 檢查結果 ====
            Debug.WriteLine($"Final Status   = {lastResult.Status}  (2 = 校正完成)");
            Debug.WriteLine($"Final Completed= {lastResult.Completed}, Required={lastResult.Required}");
            Debug.WriteLine($"Final RMSE     = {lastResult.Rmse}");

            var calibState = coord.State;
            if (!calibState.ByConn.TryGetValue(conn, out CalibData calibData))
            {
                Debug.WriteLine("CalibData not found for conn.");
                return;
            }

            if (!calibData.PixelToReal.TryGetValue(cam, out VectorX v))
            {
                Debug.WriteLine("PixelToReal not found for cam.");
                return;
            }

            Debug.WriteLine($"Affine PixelToReal: A={v.a}, B={v.b}, C={v.c}, D={v.d}, E={v.e}, F={v.f}");
        }
    }


}
