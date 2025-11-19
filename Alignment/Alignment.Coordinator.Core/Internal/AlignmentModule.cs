using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Alignment.Coordinator.Core.Abstractions;
using Alignment.Coordinator.Core.Agents;

namespace Alignment.Coordinator.Core.Internal
{
    // 對外入口：接 PLC 指令 → 依 (conn/cam) 導向對應的 Alignment Event Queue
    public sealed class AlignmentModule
    {
        private readonly IAlignmentCoordinator _coord;
        private readonly ConcurrentDictionary<string, CameraAgent> _agents
            = new ConcurrentDictionary<string, CameraAgent>(StringComparer.OrdinalIgnoreCase);

        public AlignmentModule(IAlignmentCoordinator coordinator)
        {
            _coord = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        }

        public async Task<CommandResult> HandleCommandAsync(string conn, string cam, string rawCommand, CancellationToken ct = default)
        {
            var pkt = Internal.CommandParser.Parse(rawCommand, conn, cam); // 產生 JobId
            var ok = await GetAgent(conn, cam).EnqueueAsync(pkt, ct).ConfigureAwait(false);
            return new CommandResult { Success = ok, Status = ok ? 0 : -1, Message = ok ? "Enqueued" : "Queue full", JobId = pkt.JobId };
        }

        public async Task<CommandResult> HandlePacketAsync(CommandPacket pkt, CancellationToken ct = default)
        {
            var ok = await GetAgent(pkt.Conn, pkt.Cam ?? string.Empty).EnqueueAsync(pkt, ct).ConfigureAwait(false);
            return new CommandResult { Success = ok, Status = ok ? 0 : -1, Message = ok ? "Enqueued" : "Queue full", JobId = pkt.JobId };
        }

        public CameraAgent GetAgent(string conn, string cam)
        {
            var key = $"{conn}/{cam}";
            return _agents.GetOrAdd(key, _ => new CameraAgent(conn, cam, _coord));
        }

        // 通用多相機：直接繞過單相機 queue，由 Coordinator 同步聚合（或按需求逐相機入列）
        public Task<IDictionary<string, Alignment.Core.P3>> AcquireManyAsync(string conn, IList<string> cams, TimeSpan timeout, CancellationToken ct)
        {
            // 可依需要改為逐相機入列 Measure 事件，再以 JobId 聚合
            throw new NotImplementedException("可依你的 Acquisition 設計決定使用哪條路徑");
        }
    }
}
