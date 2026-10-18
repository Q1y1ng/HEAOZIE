// 「游戏加速」页 —— M3 接入。M0 先把目标与边界写在界面上，避免空白页。

using System.Windows.Forms;

namespace Hz.Panels
{
    internal sealed class BoosterPanel : PanelBase
    {
        public override string Title { get { return "游戏加速"; } }

        public BoosterPanel(IHzHost host) : base(host)
        {
            AddCard("游戏加速", "M3 里程碑接入",
                "从 Python 版 gaming_booster（860 + 786 行 tkinter）重构而来，计划包含：\n\n" +
                "· 实时状态：CPU / 内存 / 磁盘 / GPU（nvidia-smi）/ CPU 温度，2 秒刷新\n" +
                "· 一键 Turbo：按分类结束后台进程 → 整理内存 → 清理临时文件 → 切高性能电源计划 → 刷新 DNS\n" +
                "· 分类勾选与白名单：浏览器 / 聊天 / 下载 / 音乐 / 办公 / 云同步 / 开发 / 游戏启动器\n" +
                "· 日志面板：每一步的结果可追溯（成功 / 跳过 / 失败原因）\n" +
                "· 恢复平衡电源计划、UAC 提权入口（提权重启后自动回到本页）",
                210);

            AddCard("重构带来的三个确定性改进", "对照 Python 版",
                "1) 不再需要 Python 与 psutil —— 原版在未装 psutil 的机器上直接 ModuleNotFoundError；\n" +
                "2) 不再有 cmd 控制台窗口（本应用是 winexe 目标，进程本身就是 GUI 进程）；\n" +
                "3) 内存整理与垃圾清理复用 pc-keeper 已在用的同一套做法，不再有两份实现各写一遍。",
                130);
        }
    }
}
