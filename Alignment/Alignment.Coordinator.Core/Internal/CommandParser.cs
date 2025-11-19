using System;
using System.Globalization;
using Alignment.Coordinator.Core.Abstractions;

namespace Alignment.Coordinator.Core.Internal
{
    public static class CommandParser
    {
        // 例: "command:2, robotpointx:100, robotpointy:200, robotpointu:0.5"
        public static CommandPacket Parse(string raw, string conn, string cam, string jobId = null)
        {
            if (string.IsNullOrWhiteSpace(raw)) throw new ArgumentException("raw");
            var parts = raw.Split(',');
            int? cmd = null; double? rx = null, ry = null, ru = null;

            foreach (var p in parts)
            {
                var kv = p.Split(':');
                if (kv.Length != 2) continue;
                var k = kv[0].Trim().ToLowerInvariant();
                var v = kv[1].Trim();

                if (k == "command" && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ci)) cmd = ci;
                else if (k == "robotpointx" && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var dx)) rx = dx;
                else if (k == "robotpointy" && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var dy)) ry = dy;
                else if (k == "robotpointu" && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var du)) ru = du;
            }

            if (!cmd.HasValue) throw new ArgumentException("missing command");
            if (!rx.HasValue || !ry.HasValue || !ru.HasValue) { rx = rx ?? 0; ry = ry ?? 0; ru = ru ?? 0; } // 允許非校正流程略過

            return new CommandPacket
            {
                Conn = conn,
                Cam = cam,
                Command = (AlignCommand)cmd.Value,
                JobId = jobId ?? Guid.NewGuid().ToString("N"),
                RobotX = rx.Value,
                RobotY = ry.Value,
                RobotU = ru.Value
            };
        }
    }
}
