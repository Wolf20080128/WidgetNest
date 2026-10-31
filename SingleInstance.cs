using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Threading;

namespace WidgetNest;

/// <summary>
/// 单实例闸门。
///
/// 面板是常驻托盘的，同时跑两份会出现两个托盘图标、两份抢同一个全局热键，
/// 所以第二个实例只做一件事：把已经跑着的那个叫出来，然后自己安静退出。
/// 实现用「命名互斥体 + 命名事件」：互斥体判重，事件用来通知老实例唤出面板。
/// </summary>
internal static class SingleInstance {
    /// <summary>
    /// 从托盘「以管理员身份重启」时带上的参数。
    /// 那种情况下旧实例还没退完，新实例要等它放锁，而不是把自己当成第二个实例踢掉。
    /// </summary>
    public const string RestartArgument = "--restart";

    private const string MutexName = "WidgetNest.SingleInstance";
    private const string ActivateEventName = "WidgetNest.Activate";

    private static Mutex? _mutex;
    private static EventWaitHandle? _activateEvent;
    private static Thread? _listener;

    /// <summary>
    /// 抢单实例锁。<paramref name="wait"/> 是等旧实例退出的时间（提权重启时才需要）。
    /// 返回 true 表示本进程就是唯一实例，false 表示已经有别人在跑。
    /// </summary>
    public static bool TryAcquire(TimeSpan wait) {
        Mutex mutex;

        try {
            mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);

            if (!createdNew)
                createdNew = WaitForPreviousInstance(mutex, wait);

            if (!createdNew) {
                mutex.Dispose();
                return false;
            }
        }
        catch {
            // 建不出互斥体（权限之类）就别拦着用户，当成单实例直接用
            return true;
        }

        _mutex = mutex;
        return true;
    }

    private static bool WaitForPreviousInstance(Mutex mutex, TimeSpan wait) {
        if (wait <= TimeSpan.Zero)
            return false;

        try {
            return mutex.WaitOne(wait);
        }
        catch (AbandonedMutexException) {
            // 旧实例是被强杀的，锁跟着废了，现在归我们
            return true;
        }
    }

    /// <summary>
    /// 让已经跑着的实例唤出面板。返回 false 表示没叫到（对方恰好刚退出）。
    /// </summary>
    public static bool SignalRunningInstance() {
        // 新实例这会儿才有「抢前台」的资格，把它借给老实例，否则唤出来的面板会被系统压到后面
        GrantForegroundRights();

        // 老实例的监听线程可能还没起来，短暂重试几次
        for (int attempt = 0; attempt < 10; attempt++) {
            try {
                using var handle = EventWaitHandle.OpenExisting(ActivateEventName);
                handle.Set();
                return true;
            }
            catch (WaitHandleCannotBeOpenedException) {
                Thread.Sleep(100);
            }
            catch {
                return false;
            }
        }

        return false;
    }

    /// <summary>开始监听「叫我一下」的请求；只在拿到锁的实例里调用。</summary>
    public static void StartListening(Dispatcher dispatcher, Action onActivate) {
        try {
            _activateEvent = new EventWaitHandle(
                initialState: false, EventResetMode.AutoReset, ActivateEventName);
        }
        catch {
            // 建不出事件的话第二个实例就只会静默退出，不影响主流程
            return;
        }

        _listener = new Thread(() => {
            while (true) {
                try {
                    if (!_activateEvent.WaitOne())
                        return;
                }
                catch (ObjectDisposedException) {
                    return;
                }

                // 监听线程不能碰界面，丢回 UI 线程
                dispatcher.BeginInvoke(onActivate);
            }
        }) {
            IsBackground = true,
            Name = "WidgetNest.SingleInstance"
        };

        _listener.Start();
    }

    /// <summary>退出时放锁，下一个实例才起得来。</summary>
    public static void Release() {
        _activateEvent?.Dispose();
        _activateEvent = null;
        _listener = null;

        try {
            _mutex?.ReleaseMutex();
        }
        catch {
            // 没持有时释放会抛，忽略即可
        }

        _mutex?.Dispose();
        _mutex = null;
    }

    /// <summary>
    /// 把「可以设置前台窗口」的权限转给已经在跑的那个进程。
    /// 不做这一步，老实例收到通知后 Show + Activate 也大概率被系统压回后面。
    /// </summary>
    private static void GrantForegroundRights() {
        try {
            string? exe = Environment.ProcessPath;

            if (string.IsNullOrEmpty(exe))
                return;

            string name = Path.GetFileNameWithoutExtension(exe);
            int self = Environment.ProcessId;

            foreach (var process in Process.GetProcessesByName(name)) {
                using (process) {
                    if (process.Id == self)
                        continue;

                    NativeMethods.AllowSetForegroundWindow(process.Id);
                    return;
                }
            }
        }
        catch {
            // 拿不到别的进程就退回默认行为，最多是面板没抢到前台
        }
    }
}
