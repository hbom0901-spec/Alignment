using System.Threading.Tasks;
namespace Core.Abstractions
{
    public interface IMotionService
    {
        // 連線與初始化
        Task<bool> ConnectAsync();
        Task DisconnectAsync();

        // 場景 A：絕對移動 (例如：回 Home，或移動到固定的拍照點)
        // 適用於：EtherCAT 直接控制、或 Robot 走到固定位置
        Task MoveAbsoluteAsync(double x, double y, double u);

        // 場景 B：相對移動 / 補正 (例如：視覺算出來差 0.1mm，要移動 0.1mm)
        // 適用於：你的 TCP/IP 告知補正值場景
        Task MoveRelativeAsync(double dx, double dy, double du);

        // 取得目前位置 (對位計算通常需要知道現在在哪)
        Task<(double x, double y, double u)> GetPositionAsync();
        Task HomeAsync();
    }
}
