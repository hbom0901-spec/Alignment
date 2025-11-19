using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Alignment.Coordinator.Core.Abstractions;
using Alignment.Core;

namespace Alignment.Coordinator.Core.Agents
{
    // Alignment Event Queue + Alignment StateMachine（序列化）
    public sealed class CameraAgent : IDisposable
    {
        private readonly string _conn, _cam;
        private readonly IAlignmentCoordinator _coord;
        private readonly ActionBlock<CommandPacket> _queue; // Alignment Event Queue

        public string Conn => _conn;
        public string Cam => _cam;

        public CameraAgent(string conn, string cam, IAlignmentCoordinator coord, int capacity = 256)
        {
            _conn = conn; _cam = cam; _coord = coord;

            var opt = new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = 1,
                EnsureOrdered = true,
                BoundedCapacity = capacity
            };

            _queue = new ActionBlock<CommandPacket>(async pkt =>
            {
                // Alignment StateMachine：以 pkt.Command 控制狀態轉移
                await _coord.HandleAsync(pkt).ConfigureAwait(false);
            }, opt);
        }

        public Task<bool> EnqueueAsync(CommandPacket pkt, CancellationToken ct = default)
            => _queue.SendAsync(pkt, ct);

        public void Complete() => _queue.Complete();
        public void Dispose() => _queue.Complete();
    }
}
