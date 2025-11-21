using System.Threading.Tasks;
using Alignment.Core;

namespace Core.Abstractions
{
    // 定義視覺結果的資料結構
    public class VisionResult
    {
        public bool Success { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Theta { get; set; } // 角度
        public double Score { get; set; } // 分數
        public string Message { get; set; }
    }

    // 定義相機行為
    public interface IVisionService
    {
        // 連線相機
        Task<bool> ConnectAsync();

        // 拍照並辨識 (傳入配方中的特徵名稱)
        Task<P3> CaptureAsync(string camName, System.Threading.CancellationToken ct = default);

        // 斷線
        Task DisconnectAsync();
    }
}