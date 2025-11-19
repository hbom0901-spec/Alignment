using System.Threading;
using System.Threading.Tasks;
using Alignment.Core;

namespace Alignment.Coordinator.Core.Abstractions
{
    // 若你已有同責接口，改 using 指向你現有命名空間即可
    public interface IVisionService
    {
        // 內部可實作 Grab+Analyze；或你也可分拆 Acquisition/Analysis
        Task<P3> CaptureAsync(string cam, CancellationToken ct);
    }

    public interface IVisionProvider
    {
        IVisionService Get(string cam); // 回傳對應相機的實作（或共用一實例）
    }

    public interface IServoController { /* 保留。Coordinator 預設只回 offset。*/ }

    public interface ISystemLogger
    {
        void Info(string tag, object data = null);
        void Warn(string tag, object data = null);
        void Error(string tag, object data = null);
    }
}
