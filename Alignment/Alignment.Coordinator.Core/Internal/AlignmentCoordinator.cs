using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alignment.Core;
using Alignment.Coordinator.Core.Abstractions;
using Alignment.Coordinator.Core.State;
using System.Diagnostics;

namespace Alignment.Coordinator.Core.Internal
{
    // 純流程與演算法整合，不含事件佇列；由 Agent 單序列呼叫
    public sealed class AlignmentCoordinator : IAlignmentCoordinator
    {
        private readonly CoordinatorState _state = new CoordinatorState();
        private readonly IAlignmentService _svc;

        private IVisionProvider _visionProvider;
        private IServoController _servo;
        private ISystemLogger _log;
        private IAlignmentConfig _cfg;

        public AlignmentState State => _svc.Snapshot();
        //public AlignmentState State => _state.Calibs;
        public AlignmentCoordinator(IAlignmentService svc)
        {
            _svc = svc ?? throw new ArgumentNullException(nameof(svc));
        }

        public void SetVision(IVisionService vision) => _visionProvider = new SingleVisionProvider(vision);
        public void SetVisionProvider(IVisionProvider provider) => _visionProvider = provider;
        public void SetServo(IServoController servo) => _servo = servo;
        public void SetLogger(ISystemLogger logger) => _log = logger;
        public void SetConfig(IAlignmentConfig config) => _cfg = config;

        // --- Command entry (由 Agent 單序列呼叫) ---
        public async Task<CommandResult> HandleAsync(CommandPacket cmd, CancellationToken ct = default)
        {
            if (cmd == null) throw new ArgumentNullException("cmd");
            _state.LastRobotPointByConn[cmd.Conn] = new P3 { X = cmd.RobotX, Y = cmd.RobotY, U = cmd.RobotU };

            switch (cmd.Command)
            {
                case AlignCommand.Calibrate: return await CalibrateOnceAsync(cmd.Conn, cmd.Cam, cmd.JobId, ct);
                case AlignCommand.Register: return await RegisterAsync(cmd.Conn, cmd.Cams ?? new[] { cmd.Cam }, cmd.JobId, ct);
                case AlignCommand.Align: return await AlignAsync(cmd.Conn, cmd.Cams ?? new[] { cmd.Cam }, cmd.JobId, ct);
                case AlignCommand.Reset: return Reset(cmd.Conn, cmd.Cam, ResetScope.ProgressOnly, cmd.JobId);
                default:
                    return Fail(cmd.JobId, "Unknown command");
            }
        }

        // --- Calibrate: 單次累積一對 ---
        private async Task<CommandResult> CalibrateOnceAsync(string conn, string cam, string jobId, CancellationToken ct)
        {
            if (_svc == null)
                throw new InvalidOperationException("IAlignmentService is not set for AlignmentCoordinator.");

            var vision = _visionProvider?.Get(cam);
            if (vision == null) return Fail(jobId, "Vision not set");

            // 1. 外部回報的實際 Robot 點，只用於第一次當 BaseRobot 記錄
            if (!_state.LastRobotPointByConn.TryGetValue(conn, out P3 realActual))
            {
                realActual = new P3();
            }

            // 2. 第一次收到 Calibrate：設定 BaseRobot / PosList / Steps / RequiredCount
            (string conn, string cam) key = (conn, cam);
            if (!_state.CalibPlans.TryGetValue(key, out CalibPlan plan))
            {
                plan = new CalibPlan();
                _state.CalibPlans[key] = plan;
            }

            if (!plan.BaseRobotSet)
            {
                plan.BaseRobot = realActual;
                plan.BaseRobotSet = true;

                if (plan.PosList == null || plan.PosList.Count == 0)
                    plan.PosList = BuildCalibPosList();

                if (plan.Steps == null || plan.Steps.Count == 0)
                    plan.Steps = BuildCalibSteps();

                if (plan.PosList != null && plan.PosList.Count > 0)
                {
                    // 目前流程只用前 12 點做校正（9 仿射 + 3 旋心）
                    plan.RequiredCount = Math.Min(plan.PosList.Count, 12);
                }
            }

            int idx = plan.CompletedCount;
            List<P3> posList = plan.PosList ?? new List<P3>();
            List<P3> steps = plan.Steps ?? new List<P3>();

            // 4. 拍照，取得 CCD 點
            P3 ccd = await vision.CaptureAsync(cam, ct).ConfigureAwait(false);

            // 建立 pair：(ccd,  posList[idx])
            plan.Pairs.Add((ccd, posList[idx]));

            _log?.Info("Calibrate.Pair", (conn, cam, jobId, Index: idx, Real: posList[idx]));

            int required = plan.RequiredCount == 0 ? 0 : plan.RequiredCount;
            int completed = plan.CompletedCount;

            // 5. 還沒收集完 → 回「下一步移動量」給外部
            if (completed < Math.Max(3, required == 0 ? 3 : required))
            {
                // 注意：這裡的 NextRobot 還是沿用原本邏輯
                int nextIndex = Math.Max(0, completed - 1);
                P3 nextRobot = (steps.Count > nextIndex) ? steps[nextIndex] : new P3();

                return new CommandResult
                {
                    Success = true,
                    Status = 1,
                    Message = "Calibrating",
                    JobId = jobId,
                    Completed = completed,
                    Required = required,
                    Pixel = ccd,
                    NextRobot = nextRobot
                };
            }

            // 6. 收集完 → 交給 Core Service 解仿射 + 旋轉中心並寫入 AlignmentState
            const int affineCount = 9;
            const int rotationCount = 3;

            // 正常流程下 plan.Pairs.Count 應該就是 RequiredCount（預期為 12）
            // CalibrateFromPairs 會更新內部 AlignmentState 的 CalibData
            CalibInfo info = _svc.CalibrateFromPairs(
                conn,
                cam,
                plan.Pairs,
                affineCount,
                rotationCount,
                out double affineRmse,
                out P3 rotationCenter,
                out double rotationRmse);

            plan.IsDone = true;

            _log?.Info("Calibrate.Done", new
            {
                Conn = conn,
                Cam = cam,
                JobId = jobId,
                Rmse = affineRmse,
                Rc = rotationCenter,
                RcRmse = rotationRmse,
                Pairs = plan.CompletedCount,
                CalibInfo = info
            });

            int lastIndex = Math.Max(0, completed - 1);
            P3 lastStep = (steps.Count > lastIndex) ? steps[lastIndex] : new P3();

            return new CommandResult
            {
                Success = true,
                Status = 2,
                Message = "Calibration completed",
                JobId = jobId,
                Rmse = affineRmse,
                Completed = plan.CompletedCount,
                Required = plan.RequiredCount,
                NextRobot = lastStep
            };
        }


        // --- Register: 多相機聚合 ---
        private async Task<CommandResult> RegisterAsync(string conn, IList<string> cams, string jobId, CancellationToken ct)
        {
            if (cams == null || cams.Count == 0) return Fail(jobId, "No cams");

            var key = (conn, jobId);
            var plan = _state.RegisterPlans.GetOrAdd(key, _ => new RegisterPlan
            {
                ExpectedCams = new System.Collections.Generic.HashSet<string>(cams, StringComparer.OrdinalIgnoreCase),
                RealGoldenSnapshot = _state.LastRobotPointByConn.TryGetValue(conn, out var rr) ? rr : new P3(),
                DeadlineUtc = DateTime.UtcNow.AddSeconds(3)
            });

            foreach (var cam in cams)
            {
                if (plan.Pixels.ContainsKey(cam)) continue;
                var vision = _visionProvider?.Get(cam);
                if (vision == null) return Fail(jobId, $"Vision not set: {cam}");
                var px = await vision.CaptureAsync(cam, ct).ConfigureAwait(false);
                plan.Pixels[cam] = px;
                _log?.Info("Register.Pixel", new { conn, cam, jobId });
            }

            if (plan.ExpectedCams.IsSubsetOf(plan.Pixels.Keys))
            {
                // 全到齊 → 透過 Core Service 寫入 Golden
                if (_svc == null)
                    return Fail(jobId, "AlignmentService is not set");

                foreach (var cam in plan.ExpectedCams)
                {
                    var px = plan.Pixels[cam];
                    // 用 Core 提供的 API 寫 PixelGolden + RealGolden
                    _svc.RegisterGolden(
                        conn,
                        cam,
                        new List<P3> { px },
                        plan.RealGoldenSnapshot);
                }

                _state.RegisterPlans.TryRemove(key, out _);
                _log?.Info("Register.Done", new { conn, jobId, cams });

                return new CommandResult
                {
                    Success = true,
                    Status = 2,
                    Message = "Register completed",
                    JobId = jobId,
                    Real = plan.RealGoldenSnapshot
                };
            }

            return new CommandResult
            {
                Success = true,
                Status = 1,
                Message = "Register pending",
                JobId = jobId,
                Completed = plan.Pixels.Count,
                Required = plan.ExpectedCams.Count
            };
        }

        // --- Align: 多相機聚合 ---
        private async Task<CommandResult> AlignAsync(string conn, IList<string> cams, string jobId, CancellationToken ct)
        {
            if (cams == null || cams.Count == 0) return Fail(jobId, "No cams");
            var calibs = _svc.Snapshot();
            if (!calibs.ByConn.TryGetValue(conn, out CalibData calib))
            {
                return Fail(jobId, $"No calibration found for connection '{conn}'");
            }
            foreach (var cam in cams)
                if (!calib.PixelToReal.ContainsKey(cam)) return Fail(jobId, $"No calibration matrix: {cam}");

            // 觸發拍照
            var pixels = new Dictionary<string, P3>(StringComparer.OrdinalIgnoreCase);
            foreach (var cam in cams)
            {
                var vision = _visionProvider?.Get(cam);
                if (vision == null) return Fail(jobId, $"Vision not set: {cam}");
                pixels[cam] = await vision.CaptureAsync(cam, ct).ConfigureAwait(false);
            }

            // Pixel→Real
            var reals = new Dictionary<string, P3>(StringComparer.OrdinalIgnoreCase);
            foreach (var cam in cams)
                reals[cam] = VectorXOps.Transform(calib.PixelToReal[cam], pixels[cam]);

            // 
            var fused = reals[cams[0]];
            var tgt = calib.RealGolden;
            var off = new P3 { X = fused.X - tgt.X, Y = fused.Y - tgt.Y, U = fused.U - tgt.U };

            _log?.Info("Align.Done", new { conn, jobId, cams, fused, off });

            return new CommandResult { Success = true, Status = 0, Message = "OK", JobId = jobId, Real = fused, Offset = off };
        }

        // --- Utilities ---
        //private CalibData GetCalib(string conn)
        //{
        //    if (!_state.Calibs.ByConn.TryGetValue(conn, out var c)) _state.Calibs.ByConn[conn] = c = new CalibData();
        //    return c;
        //}

        private static CommandResult Fail(string jobId, string msg)
            => new CommandResult { Success = false, Status = 3, Message = msg, JobId = jobId };

        public CommandResult Reset(string conn, string cam, ResetScope scope, string jobId)
        {
            // 1) 清在途暫存（只影響流程，不碰 Core state）
            if (cam == null)
            {
                foreach (var k in _state.CalibPlans.Keys)
                    if (k.conn == conn)
                        _state.CalibPlans.TryRemove(k, out _);

                foreach (var k in _state.RegisterPlans.Keys)
                    if (k.conn == conn)
                        _state.RegisterPlans.TryRemove(k, out _);

                foreach (var k in _state.AlignJobs.Keys)
                    if (k.conn == conn)
                        _state.AlignJobs.TryRemove(k, out _);
            }
            else
            {
                _state.CalibPlans.TryRemove((conn, cam), out _);
                // RegisterPlans / AlignJobs 是以 (conn, jobId) 為 key，這裡通常不用特別處理 cam
            }

            // 2) 需要清掉校正 → 交給 Core Service
            if (scope == ResetScope.AllCalibration && _svc != null)
            {
                _svc.ClearCalibration(conn);
            }

            _log?.Info("Reset", new { conn, cam, scope });
            return new CommandResult { Success = true, Status = 0, Message = "Reset", JobId = jobId };
        }


        private List<P3> BuildCalibSteps()
        {
            if (_cfg == null) return new List<P3>();

            var consts = _cfg.LoadConstants() ?? new AlignmentConstants();
            var parms = _cfg.LoadParams() ?? new AlignmentParams();

            var mv = parms.CalibMove ?? new P3();
            var list = new List<P3>();

            if (consts.CalibMoveMatrix != null)
            {
                foreach (var m in consts.CalibMoveMatrix)
                {
                    list.Add(new P3
                    {
                        X = m.X * mv.X,
                        Y = m.Y * mv.Y,
                        U = m.U * mv.U
                    });
                }
            }

            return list;
        }
        private List<P3> BuildCalibPosList()
        {
            if (_cfg == null) return new List<P3>();

            var consts = _cfg.LoadConstants() ?? new AlignmentConstants();
            var parms = _cfg.LoadParams() ?? new AlignmentParams();

            var mv = parms.CalibMove ?? new P3();
            var list = new List<P3>();

            if (consts.CalibPosMatrix != null)
            {
                foreach (var m in consts.CalibPosMatrix)
                {
                    list.Add(new P3
                    {
                        X = m.X * mv.X,
                        Y = m.Y * mv.Y,
                        U = m.U * mv.U
                    });
                }
            }

            return list;
        }

        // --- Direct APIs (for tests/tools) ---
        public Task<CalibResult> BuildPixelToRealAsync(
            string conn,
            string cam,
            IReadOnlyList<P3> ccd,
            IReadOnlyList<P3> rob,
            CancellationToken ct = default)
        {
            if (ccd == null || rob == null || ccd.Count != rob.Count || ccd.Count < 3)
                throw new ArgumentException("Need ≥3 pairs");

            if (_svc == null)
                throw new InvalidOperationException("IAlignmentService is not set.");

            // 組成 pairs 給 Core 的 CalibrateFromPairs
            var pairs = new List<(P3 ccd, P3 rob)>(ccd.Count);
            for (int i = 0; i < ccd.Count; i++)
                pairs.Add((ccd[i], rob[i]));

            // 這裡只做仿射，不算旋心 → rotationCount = 0
            CalibInfo info = _svc.CalibrateFromPairs(
                conn,
                cam,
                pairs,
                affineCount: ccd.Count,
                rotationCount: 0,
                out double affineRmse,
                out _,
                out _);

            // 從 Core 的 AlignmentState 把最新解出來的 v / inv 讀出
            var snapshot = _svc.Snapshot();
            if (!snapshot.ByConn.TryGetValue(conn, out CalibData calib))
                throw new InvalidOperationException($"No calibration entry for connection '{conn}'.");

            if (!calib.PixelToReal.TryGetValue(cam, out VectorX v))
                throw new InvalidOperationException($"No PixelToReal for '{conn}/{cam}'.");

            if (!calib.RealToPixel.TryGetValue(cam, out VectorX inv))
                throw new InvalidOperationException($"No RealToPixel for '{conn}/{cam}'.");

            _log?.Info("BuildPixelToReal", new { conn, cam, rmse = affineRmse, n = ccd.Count });

            var result = new CalibResult
            {
                Cam = cam,
                PixelToReal = v,
                RealToPixel = inv,
                RotDeg = info.ThetaDeg,
                Sx = info.Sx,
                Sy = info.Sy,
                Shear = info.Shear,
                Rmse = affineRmse,
                PairCount = ccd.Count
            };

            // 這個方法實際沒有非同步行為，用 Task.FromResult 包一層即可
            return Task.FromResult(result);
        }


        // 單一 Vision 實例包裝成 Provider
        private sealed class SingleVisionProvider : IVisionProvider
        {
            private readonly IVisionService _vision;
            public SingleVisionProvider(IVisionService v) { _vision = v; }
            public IVisionService Get(string cam) => _vision;
        }
    }
}
