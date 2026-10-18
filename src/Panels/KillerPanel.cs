// 「定时关闭应用」页 —— M2 接入。原 Python 版 app_killer.py（724 行 tkinter）的功能对照。

using System.Windows.Forms;

namespace Hz.Panels
{
    internal sealed class KillerPanel : PanelBase
    {
        public override string Title { get { return "定时关闭应用"; } }

        public KillerPanel(IHzHost host) : base(host)
        {
            AddCard("定时关闭应用", "M2 里程碑接入",
                "勾选要结束的进程，设定倒计时，到点自动关闭。\n\n" +
                "· 进程列表：名称 / PID / 内存占用，带勾选框，可全选、取消、刷新\n" +
                "· 点选目标：点一下「点击选择」再点任意窗口，直接选中它所属的进程\n" +
                "· 预设 5 / 15 / 30 / 60 分钟，倒计时可暂停、可重置\n" +
                "· 到点前 5 秒可取消；托盘常驻，关掉窗口定时照样生效",
                175);

            AddCard("安全边界", "M2 验收点",
                "· 系统关键进程（svchost / csrss / lsass / winlogon / dwm / explorer 等）不在列表里，\n" +
                "  也不允许通过点选加入，避免误关导致桌面崩溃；\n" +
                "· 结束进程前二次确认要关闭的目标清单；\n" +
                "· 结束失败（权限不足 / 进程已退出）逐条记录原因，不静默失败。",
                135);
        }
    }
}
