// 「定时电源」页 —— M1 接入。原 Python 版 sleep_timer.py（443 行 tkinter）的功能对照。

using System.Windows.Forms;

namespace Hz.Panels
{
    internal sealed class PowerPanel : PanelBase
    {
        public override string Title { get { return "定时电源"; } }

        public PowerPanel(IHzHost host) : base(host)
        {
            AddCard("定时电源", "M1 里程碑接入",
                "设定倒计时，到点执行 睡眠 / 关机 / 重启，执行前留 5 秒反悔窗口。\n\n" +
                "· 大号倒计时（等宽字体）+ 开始 / 暂停 / 重置\n" +
                "· 预设 15 / 30 / 60 / 120 分钟，也可自己填时分秒\n" +
                "· 托盘常驻：关掉窗口定时照样生效，托盘提示剩余时间\n" +
                "· 到点前 5 秒显示「点击取消」，可撤销",
                170);

            AddCard("要修掉的原版缺陷：递减计数会漂移", "M1 验收点",
                "原版用「每秒把剩余秒数减一」的递减计数。机器一旦睡眠/休眠，计数器就停了，\n" +
                "唤醒后实际执行时间比设定值晚——设定 30 分钟、合盖睡 2 小时，醒来还得再等 30 分钟。\n\n" +
                "新实现改成「记下绝对目标时刻，每次比较当前时间」，睡眠唤醒、系统时间调整后\n" +
                "剩余时间自动校正，不再漂移。",
                150);
        }
    }
}
