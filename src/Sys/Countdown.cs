// 倒计时引擎：记「绝对目标时刻」，每次拿当前时间比 —— 不是「每秒减一」。
//
// 为什么必须这样（这是原 Python 版的真实缺陷）：递减计数在系统睡眠/休眠时停摆。
// 设定 30 分钟、合盖睡 2 小时，唤醒后计数器还剩 30 分钟，于是「定时关机」比
// 用户预期晚了整整两小时。用绝对时刻，唤醒后目标已过 → 立刻到点。
//
// 时钟是可注入的（默认 DateTime.Now），所以自检能模拟「睡眠 3 小时」而不真睡。

using System;

namespace Hz.Sys
{
    internal sealed class Countdown
    {
        private readonly Func<DateTime> _clock;
        private DateTime _deadline;
        private TimeSpan _pausedRemaining;
        private bool _armed;
        private bool _paused;

        public Countdown() : this(null) { }

        public Countdown(Func<DateTime> clock)
        {
            _clock = clock ?? delegate { return DateTime.Now; };
        }

        public bool IsArmed { get { return _armed; } }
        public bool IsPaused { get { return _paused; } }
        public TimeSpan Total { get; private set; }

        /// 目标时刻（未武装时为 MinValue）。
        public DateTime Deadline { get { return _armed ? _deadline : DateTime.MinValue; } }

        public TimeSpan Remaining
        {
            get
            {
                if (!_armed) return TimeSpan.Zero;
                if (_paused) return _pausedRemaining < TimeSpan.Zero ? TimeSpan.Zero : _pausedRemaining;
                TimeSpan left = _deadline - _clock();
                return left < TimeSpan.Zero ? TimeSpan.Zero : left;
            }
        }

        public void Start(TimeSpan duration)
        {
            if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException("duration", "倒计时时长必须大于 0");
            Total = duration;
            _deadline = _clock().Add(duration);
            _armed = true;
            _paused = false;
            _pausedRemaining = TimeSpan.Zero;
        }

        public void Pause()
        {
            if (!_armed || _paused) return;
            _pausedRemaining = Remaining;
            _paused = true;
        }

        public void Resume()
        {
            if (!_armed || !_paused) return;
            _deadline = _clock().Add(_pausedRemaining);
            _paused = false;
        }

        public void Reset()
        {
            _armed = false;
            _paused = false;
            Total = TimeSpan.Zero;
            _pausedRemaining = TimeSpan.Zero;
        }

        /// 到点时返回一次 true（随后自动解除武装，避免重复触发）。
        public bool Poll()
        {
            if (!_armed || _paused) return false;
            if (_clock() < _deadline) return false;
            _armed = false;
            return true;
        }

        /// HH:mm:ss（不足一小时也补零，视觉上不跳动）。
        public static string Format(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            int hours = (int)t.TotalHours;
            return hours.ToString("00") + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00");
        }
    }
}
