using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Alignment.Core;
using System.Diagnostics;
using Core.Abstractions;

namespace Device.Simulation
{
    public class SimulatedVisionService : IVisionService
    {
        private readonly IMotionService _motion;
        private bool _isConnected;

        // 用來記錄目前走到第幾張圖
        private int _captureIndex = 0;

        // 這是您 CalibrateFlowTest / MainWindow 中的那組固定點位
        private readonly List<P3> _ccdSeq = new List<P3>
        {
            new P3 { X=1854.916, Y=1577.918 }, // 第 1 點
            new P3 { X=2724.167, Y=1574.615 }, // 第 2 點
            new P3 { X=2720.464, Y=709.261 },  // ...
            new P3 { X=1852.647, Y=717.672 },
            new P3 { X=986.073,  Y=721.674 },
            new P3 { X=989.928,  Y=1584.328 },
            new P3 { X=995.685,  Y=2445.826 },
            new P3 { X=1860.891, Y=2441.617 },
            new P3 { X=2725.389, Y=2437.412 },
            new P3 { X=1851.717, Y=1576.801 }, // 第 10 點
            new P3 { X=1829.247, Y=1585.974 }, // 第 11 點 (旋轉中心點 1)
            new P3 { X=1872.619, Y=1559.839 }, // 第 12 點 (旋轉中心點 2)
            new P3 { X=1854.916, Y=1577.918 }  // 第 13 點 (回原點驗證)
        };

        // 雖然是照表操課，但保留 Motion 注入，未來想切換回物理模式很方便
        public SimulatedVisionService(IMotionService motion)
        {
            _motion = motion;
        }

        public Task<bool> ConnectAsync()
        {
            _isConnected = true;
            _captureIndex = 0; // 連線時重置索引，從第 1 張開始
            Console.WriteLine("[SimVision] Connected (Sequence Mode).");
            return Task.FromResult(true);
        }

        public Task DisconnectAsync()
        {
            _isConnected = false;
            return Task.CompletedTask;
        }

        public async Task<P3> CaptureAsync(string camName, CancellationToken ct = default)
        {
            if (!_isConnected) throw new Exception("Camera not connected!");

            // 模擬一點延遲
            await Task.Delay(200);

            // 取得目前的點，並將索引 +1
            int i = _captureIndex;
            if (i >= _ccdSeq.Count) i = _ccdSeq.Count - 1; // 防止破表，超過就回傳最後一點

            P3 result = _ccdSeq[i];

            Debug.WriteLine($"[SimVision] Sequence[{_captureIndex + 1}/{_ccdSeq.Count}] -> Pixel({result.X:F2}, {result.Y:F2})");

            // 準備下一張
            _captureIndex++;

            // 回傳複製的物件，避免外部修改影響到內部 List
            return new P3 { X = result.X, Y = result.Y, U = result.U };
        }
    }
}