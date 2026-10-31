using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

using Microsoft.Win32.SafeHandles;

namespace WidgetNest;

/// <summary>
/// 小组件用到的系统数据。全部做缓存与降级：取不到就返回 null，
/// 上层显示「—」，绝不让一个传感器拖慢整个面板。
/// </summary>
internal static class SystemMetrics {
    // ===== CPU 占用率 =====

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime {
        public uint Low;
        public uint High;

        public readonly ulong Value => ((ulong)High << 32) | Low;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    private static ulong _prevIdle, _prevTotal;
    private static double _cpuUsage;
    private static long _cpuStamp;

    /// <summary>CPU 占用率 0–100。采样间隔不足 200ms 时直接返回上次结果。</summary>
    public static double CpuUsage() {
        long now = Environment.TickCount64;
        if (now - _cpuStamp < 200)
            return _cpuUsage;

        if (!GetSystemTimes(out var idle, out var kernel, out var user))
            return _cpuUsage;

        ulong idleTicks = idle.Value;
        ulong totalTicks = kernel.Value + user.Value;

        ulong idleDelta = idleTicks - _prevIdle;
        ulong totalDelta = totalTicks - _prevTotal;

        _prevIdle = idleTicks;
        _prevTotal = totalTicks;
        _cpuStamp = now;

        if (totalDelta > 0 && totalDelta >= idleDelta)
            _cpuUsage = Math.Clamp((totalDelta - idleDelta) * 100.0 / totalDelta, 0, 100);

        return _cpuUsage;
    }

    // ===== 内存 =====

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    /// <summary>返回 (已用 GB, 总量 GB, 百分比)。</summary>
    public static (double UsedGb, double TotalGb, double Percent) Memory() {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };

        if (!GlobalMemoryStatusEx(ref status) || status.TotalPhys == 0)
            return (0, 0, 0);

        double total = status.TotalPhys / 1024.0 / 1024 / 1024;
        double used = (status.TotalPhys - status.AvailPhys) / 1024.0 / 1024 / 1024;

        return (used, total, used / total * 100);
    }

    // ===== 磁盘 =====

    /// <summary>系统盘（已用, 总量, 百分比），单位 GB。</summary>
    public static (double UsedGb, double TotalGb, double Percent) Disk() {
        try {
            string root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            var drive = new DriveInfo(root);

            if (!drive.IsReady || drive.TotalSize == 0)
                return (0, 0, 0);

            double total = drive.TotalSize / 1024.0 / 1024 / 1024;
            double free = drive.TotalFreeSpace / 1024.0 / 1024 / 1024;

            return (total - free, total, (total - free) / total * 100);
        }
        catch {
            return (0, 0, 0);
        }
    }

    // ===== 电池 =====

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus {
        public byte ACStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    /// <summary>无电池的台式机返回 null。</summary>
    public static (int Percent, bool Charging)? Battery() {
        if (!GetSystemPowerStatus(out var status))
            return null;

        // BatteryFlag 128 = 无系统电池
        if ((status.BatteryFlag & 128) != 0)
            return null;

        int percent = status.BatteryLifePercent == 255 ? -1 : status.BatteryLifePercent;
        return (Math.Clamp(percent, 0, 100), status.ACStatus == 1);
    }

    // ===== 音量 / 静音（CoreAudio） =====

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
            [MarshalAs(UnmanagedType.Interface)] out IAudioEndpointVolume volume);
        [PreserveSig] int OpenPropertyStore(int stgmAccess, out IntPtr properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    // 方法顺序必须与 vtable 完全一致，漏一个就会调错函数
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    private static IAudioEndpointVolume? TryGetVolumeObject() {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;

        try {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();

            // eRender = 0, eMultimedia = 1
            if (enumerator.GetDefaultAudioEndpoint(0, 1, out device) != 0)
                return null;

            Guid iid = typeof(IAudioEndpointVolume).GUID;

            // CLSCTX_INPROC_SERVER = 0x1
            return device.Activate(ref iid, 1, IntPtr.Zero, out var volume) == 0
                ? volume
                : null;
        }
        catch {
            return null;
        }
        finally {
            if (device != null)
                Marshal.ReleaseComObject(device);

            if (enumerator != null)
                Marshal.ReleaseComObject(enumerator);
        }
    }

    private static float _volume = -1;
    private static bool _muted;
    private static long _volumeStamp;

    private static void RefreshVolumeCache(bool force) {
        long now = Environment.TickCount64;
        if (!force && now - _volumeStamp < 800)
            return;

        var volume = TryGetVolumeObject();
        if (volume == null)
            return;

        try {
            if (volume.GetMasterVolumeLevelScalar(out float level) == 0)
                _volume = Math.Clamp(level * 100f, 0, 100);

            if (volume.GetMute(out bool mute) == 0)
                _muted = mute;

            _volumeStamp = now;
        }
        catch {
            // 设备切换的瞬间可能失败，保持旧值
        }
        finally {
            Marshal.ReleaseComObject(volume);
        }
    }

    public static double Volume() {
        RefreshVolumeCache(force: false);
        return _volume < 0 ? 0 : _volume;
    }

    public static bool IsMuted() {
        RefreshVolumeCache(force: false);
        return _muted;
    }

    public static void SetVolume(double percent) {
        var volume = TryGetVolumeObject();
        if (volume == null)
            return;

        try {
            volume.SetMasterVolumeLevelScalar((float)Math.Clamp(percent / 100.0, 0, 1), Guid.Empty);

            // 调音量时系统会自动取消静音，缓存跟着走
            _volume = (float)Math.Clamp(percent, 0, 100);
            _volumeStamp = Environment.TickCount64;
        }
        catch {
            // 忽略：静音/独占模式下可能被拒绝
        }
        finally {
            Marshal.ReleaseComObject(volume);
        }
    }

    /// <summary>切换静音，返回切换后的状态。</summary>
    public static bool ToggleMute() {
        var volume = TryGetVolumeObject();
        if (volume == null)
            return _muted;

        try {
            if (volume.GetMute(out bool current) == 0) {
                bool next = !current;
                volume.SetMute(next, Guid.Empty);
                _muted = next;
                _volumeStamp = Environment.TickCount64;
                return next;
            }
        }
        catch {
            // 忽略
        }
        finally {
            Marshal.ReleaseComObject(volume);
        }

        return _muted;
    }

    // ===== 屏幕亮度（WMI） =====

    private static string? _brightnessInstance;
    private static int? _brightness;
    private static long _brightnessStamp;
    private static bool _brightnessUnsupported;

    private static string EscapeWmi(string text) => text.Replace("\\", "\\\\").Replace("'", "\\'");

    public static int? Brightness() {
        if (_brightnessUnsupported)
            return null;

        long now = Environment.TickCount64;
        if (now - _brightnessStamp < 3000)
            return _brightness;

        _brightnessStamp = now;

        try {
            using var searcher = new ManagementObjectSearcher(
                @"root\WMI", "SELECT InstanceName, CurrentBrightness FROM WmiMonitorBrightness");

            foreach (ManagementObject item in searcher.Get()) {
                _brightnessInstance = item["InstanceName"] as string;
                _brightness = Convert.ToInt32(item["CurrentBrightness"]);
                return _brightness;
            }

            _brightnessUnsupported = true;
        }
        catch {
            // 台式机 / 外接屏通常没有这个 WMI 类，标记后不再重试
            _brightnessUnsupported = true;
        }

        return null;
    }

    public static bool SetBrightness(int percent) {
        if (_brightnessUnsupported && _brightnessInstance == null)
            return false;

        try {
            string filter = _brightnessInstance == null
                ? ""
                : $" WHERE InstanceName='{EscapeWmi(_brightnessInstance)}'";

            using var searcher = new ManagementObjectSearcher(
                @"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods" + filter);

            foreach (ManagementObject method in searcher.Get()) {
                method.InvokeMethod("WmiSetBrightness",
                    new object[] { 0, (byte)Math.Clamp(percent, 0, 100) });

                _brightness = Math.Clamp(percent, 0, 100);
                _brightnessStamp = Environment.TickCount64;
                return true;
            }
        }
        catch {
            _brightnessUnsupported = true;
        }

        return false;
    }

    // ===== 温度（ACPI / 存储 / NVML） =====
    //
    // 温度传感器在 Windows 上分几档权限，这里按「拿不到就换下一个源」级联：
    //   1. ACPI 热区（MSAcpi_ThermalZoneTemperature）—— 读数最准，但只有管理员能读；
    //   2. 性能计数器里的热区（Win32_PerfFormattedData_Counters_ThermalZoneInformation）
    //      —— 同一个热区，普通权限也能读，是没提权时的主要数据来源；
    //   3. 存储温度（IOCTL_STORAGE_QUERY_PROPERTY，NVMe/SATA 通用；老 SATA 盘再退回 ATA SMART）
    //      —— 需要管理员打开物理磁盘。
    // 失败不永久缓存：一分钟后重试，用户提权或驱动更新后不必重启面板。

    private const long TemperatureRetryMs = 60_000;

    private const string NeedAdminNote = "需管理员权限：右键托盘图标 → 以管理员身份重启";

    private static double? _cpuTemp;
    private static string _cpuTempNote = "";
    private static long _cpuTempNextTry;

    /// <summary>CPU 温度（摄氏）。优先 ACPI 热区，没权限时退回性能计数器。</summary>
    public static double? CpuTemperature(out string note) {
        long now = Environment.TickCount64;

        if (now < _cpuTempNextTry) {
            note = _cpuTempNote;
            return _cpuTemp;
        }

        var (value, text) = ReadCpuTemperature();

        _cpuTemp = value;
        _cpuTempNote = text;

        // 取到值缓存 5 秒；取不到就一分钟后再试
        _cpuTempNextTry = now + (value.HasValue ? 5_000 : TemperatureRetryMs);

        note = text;
        return value;
    }

    private static (double? Value, string Note) ReadCpuTemperature() {
        // 1) ACPI 热区：管理员权限下最可靠，单位是 0.1K
        try {
            using var searcher = new ManagementObjectSearcher(
                @"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");

            double? hottest = HottestZone(searcher, item => Number(item["CurrentTemperature"]) / 10.0);
            if (hottest.HasValue)
                return (hottest, "");
        }
        catch {
            // 没权限或没有热区：交给下一个数据源
        }

        // 2) 性能计数器：普通权限可读；HighPrecisionTemperature 存的是 0.1K，没有就用整数开尔文
        try {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Temperature, HighPrecisionTemperature " +
                "FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation");

            double? hottest = HottestZone(searcher, item => {
                double tenths = Number(item["HighPrecisionTemperature"]);
                return tenths > 1000 ? tenths / 10.0 : Number(item["Temperature"]);
            });

            if (hottest.HasValue)
                return (hottest, "");
        }
        catch {
            // 这台机器不支持性能计数器热区
        }

        return (null, NeedAdminNote);
    }

    /// <summary>遍历热区，取最热的那个（多热区取平均会被机箱/电池传感器拉偏）。</summary>
    private static double? HottestZone(ManagementObjectSearcher searcher, Func<ManagementObject, double> kelvinOf) {
        double? hottest = null;

        foreach (ManagementObject zone in searcher.Get()) {
            double? celsius = KelvinToCelsius(kelvinOf(zone));
            if (celsius.HasValue)
                hottest = Math.Max(hottest ?? celsius.Value, celsius.Value);
        }

        return hottest;
    }

    /// <summary>开尔文转摄氏，顺带滤掉 0 和明显离谱的读数（部分机器的热区会返回 2732 这类占位值）。</summary>
    private static double? KelvinToCelsius(double kelvin) {
        if (kelvin <= 0)
            return null;

        double celsius = kelvin - 273.15;
        return celsius is > 1 and < 130 ? celsius : null;
    }

    /// <summary>WMI 数值可能是 uint / string / null，统一转成 double。</summary>
    private static double Number(object? value) {
        if (value is null)
            return 0;

        try {
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }
        catch {
            return 0;
        }
    }

    private static double? _diskTemp;
    private static string _diskTempNote = "";
    private static long _diskTempNextTry;

    /// <summary>
    /// 硬盘温度（摄氏）。NVMe 走存储温度属性，老 SATA 盘退回 ATA SMART。
    /// 两条路都要管理员权限打开物理磁盘，普通权限下会返回 null。
    /// </summary>
    public static double? DiskTemperature(out string note) {
        long now = Environment.TickCount64;

        if (now < _diskTempNextTry) {
            note = _diskTempNote;
            return _diskTemp;
        }

        var (value, text) = ReadDiskTemperature();

        _diskTemp = value;
        _diskTempNote = text;

        _diskTempNextTry = now + (value.HasValue ? 15_000 : TemperatureRetryMs);

        note = text;
        return value;
    }

    private static (double? Value, string Note) ReadDiskTemperature() {
        double? value = StorageTemperature();
        if (value.HasValue)
            return (value, "");

        value = SmartTemperature();
        if (value.HasValue)
            return (value, "");

        return (null, NeedAdminNote);
    }

    // ---- 存储温度属性：NVMe / SATA 都支持 ----

    private const uint IoctlStorageQueryProperty = 0x002D1400;
    private const uint StorageDeviceTemperature = 52;
    private const uint StorageAdapterTemperature = 51;

    private const uint FileAccessRead = 0x80000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security,
                                                    uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[] input, int inputSize,
                                               byte[] output, int outputSize, out int returned, IntPtr overlapped);

    [StructLayout(LayoutKind.Sequential)]
    private struct StoragePropertyQuery {
        public uint PropertyId;
        public uint QueryType;
        public byte Additional;
    }

    /// <summary>STORAGE_TEMPERATURE_INFO：Temperature 是摄氏，其余字段这里用不到。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct StorageTemperatureInfo {
        public ushort Index;
        public short Temperature;
        public ushort OverThreshold;
        public ushort UnderThreshold;
        public byte OverThresholdChangeable;
        public byte UnderThresholdChangeable;
        public byte Reserved0;
        public byte Reserved1;
        public ulong Event1;
    }

    private static double? StorageTemperature() {
        for (int index = 0; index < 4; index++) {
            double? value = DriveTemperature(index);
            if (value.HasValue)
                return value;
        }

        return null;
    }

    private static double? DriveTemperature(int index) {
        // 查询温度要 FILE_READ_ACCESS：未提权时 CreateFile 直接「拒绝访问」
        using SafeFileHandle handle = CreateFile($@"\\.\PhysicalDrive{index}",
            FileAccessRead, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);

        if (handle.IsInvalid)
            return null;

        foreach (uint property in new[] { StorageDeviceTemperature, StorageAdapterTemperature }) {
            var query = new StoragePropertyQuery { PropertyId = property };
            var output = new byte[256];

            var input = Structure(query);

            if (!DeviceIoControl(handle, IoctlStorageQueryProperty, input, input.Length,
                                 output, output.Length, out int returned, IntPtr.Zero))
                continue;

            if (returned < 16)
                continue;

            int count = BitConverter.ToUInt16(output, 12);
            if (count <= 0)
                continue;

            // 记录长度以描述符里的 Size 为准，避免结构体对齐差异把温度读错位置
            int total = BitConverter.ToInt32(output, 4);
            int stride = Marshal.SizeOf<StorageTemperatureInfo>();

            if (total > 16 && (total - 16) / count >= 4)
                stride = (total - 16) / count;

            for (int i = 0; i < count; i++) {
                int offset = 16 + i * stride;
                if (offset + 4 > returned)
                    break;

                short celsius = BitConverter.ToInt16(output, offset + 2);
                if (celsius is > 1 and < 130)
                    return celsius;
            }
        }

        return null;
    }

    private static byte[] Structure<T>(T value) where T : struct {
        var bytes = new byte[Marshal.SizeOf<T>()];
        IntPtr buffer = Marshal.AllocHGlobal(bytes.Length);

        try {
            Marshal.StructureToPtr(value, buffer, false);
            Marshal.Copy(buffer, bytes, 0, bytes.Length);
        }
        finally {
            Marshal.FreeHGlobal(buffer);
        }

        return bytes;
    }

    /// <summary>老 SATA 盘的 ATA SMART（NVMe 没有这个 WMI 类）。</summary>
    private static double? SmartTemperature() {
        try {
            using var searcher = new ManagementObjectSearcher(
                @"root\WMI", "SELECT VendorSpecific FROM MSStorageDriver_ATAPISmartData");

            foreach (ManagementObject drive in searcher.Get()) {
                if (drive["VendorSpecific"] is not byte[] data || data.Length < 30)
                    continue;

                // 属性区从偏移 2 起，每项 12 字节：第 0 字节是属性号，第 5 字节是原始值首字节
                for (int offset = 2; offset + 12 <= data.Length; offset += 12) {
                    if (data[offset] is not (194 or 190))
                        continue;

                    // 有些盘把历史最低/最高温写在这里，超出合理区间就跳过
                    if (data[offset + 5] is > 1 and < 130)
                        return data[offset + 5];
                }
            }
        }
        catch {
            // 没有 ATA SMART 或没权限
        }

        return null;
    }

    // ===== GPU（NVIDIA NVML） =====

    private static class Nvml {
        private delegate int InitDelegate();
        private delegate int GetCountDelegate(out uint count);
        private delegate int GetHandleDelegate(uint index, out IntPtr device);
        private delegate int GetUtilizationDelegate(IntPtr device, out UtilizationRates utilization);
        private delegate int GetTemperatureDelegate(IntPtr device, int sensor, out uint celsius);
        private delegate int GetNameDelegate(IntPtr device, StringBuilder name, uint length);

        [StructLayout(LayoutKind.Sequential)]
        internal struct UtilizationRates {
            public uint Gpu;
            public uint Memory;
        }

        private static long _nextTry;
        private static IntPtr _device = IntPtr.Zero;

        private static InitDelegate? _init;
        private static GetHandleDelegate? _getHandle;
        private static GetUtilizationDelegate? _getUtilization;
        private static GetTemperatureDelegate? _getTemperature;
        private static GetNameDelegate? _getName;

        internal static bool Ready { get; private set; }

        internal static void EnsureInitialized() {
            if (Ready)
                return;

            // 初始化失败也隔一分钟再试：驱动更新、显卡热插拔后能自己恢复
            long now = Environment.TickCount64;
            if (now < _nextTry)
                return;

            _nextTry = now + TemperatureRetryMs;

            if (!NativeLibrary.TryLoad("nvml.dll", out IntPtr lib))
                return;

            _init = Load<InitDelegate>(lib, "nvmlInit_v2", "nvmlInit");
            var getCount = Load<GetCountDelegate>(lib, "nvmlDeviceGetCount_v2", "nvmlDeviceGetCount");
            _getHandle = Load<GetHandleDelegate>(lib, "nvmlDeviceGetHandleByIndex_v2", "nvmlDeviceGetHandleByIndex");

            _getUtilization = Load<GetUtilizationDelegate>(lib, "nvmlDeviceGetUtilizationRates");
            _getTemperature = Load<GetTemperatureDelegate>(lib, "nvmlDeviceGetTemperature");
            _getName = Load<GetNameDelegate>(lib, "nvmlDeviceGetName");

            if (_init == null || getCount == null || _getHandle == null)
                return;

            if (_init() != 0 || getCount(out uint count) != 0 || count == 0)
                return;

            if (_getHandle(0, out IntPtr device) != 0)
                return;

            _device = device;
            Ready = true;
        }

        private static T? Load<T>(IntPtr lib, params string[] names) where T : Delegate {
            foreach (string name in names) {
                if (NativeLibrary.TryGetExport(lib, name, out IntPtr address))
                    return Marshal.GetDelegateForFunctionPointer<T>(address);
            }

            return null;
        }

        internal static double? Utilization() {
            EnsureInitialized();
            if (!Ready || _getUtilization == null)
                return null;

            return _getUtilization(_device, out UtilizationRates value) == 0
                ? value.Gpu
                : null;
        }

        internal static double? Temperature() {
            EnsureInitialized();
            if (!Ready || _getTemperature == null)
                return null;

            // 0 = NVML_TEMPERATURE_GPU
            return _getTemperature(_device, 0, out uint celsius) == 0
                ? celsius
                : null;
        }

        internal static string Name() {
            EnsureInitialized();
            if (!Ready || _getName == null)
                return "";

            var buffer = new StringBuilder(96);
            return _getName(_device, buffer, 96) == 0 ? buffer.ToString() : "";
        }
    }

    public static double? GpuUsage() => Nvml.Utilization();

    private static double? _smiTemp;
    private static long _smiNextTry;

    /// <summary>显卡温度（摄氏）。NVML 拿不到时退回 nvidia-smi（同样随驱动安装，不需要提权）。</summary>
    public static double? GpuTemperature(out string note) {
        double? value = Nvml.Temperature();

        if (value == null)
            value = NvidiaSmiTemperature();

        note = value.HasValue ? "" : "未检测到 NVIDIA 显卡";
        return value;
    }

    private static double? NvidiaSmiTemperature() {
        long now = Environment.TickCount64;
        if (now < _smiNextTry)
            return _smiTemp;

        _smiTemp = null;

        try {
            var info = new ProcessStartInfo("nvidia-smi.exe",
                "--query-gpu=temperature.gpu --format=csv,noheader,nounits") {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(info);

            if (process != null) {
                string output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit(1500);

                string first = output.Split('\n')[0].Trim();

                if (double.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out double celsius) &&
                    celsius is > 1 and < 130)
                    _smiTemp = celsius;
            }
        }
        catch {
            // 没有 nvidia-smi（AMD / Intel 显卡）就当作不可用
        }

        _smiNextTry = now + (_smiTemp.HasValue ? 10_000 : TemperatureRetryMs);
        return _smiTemp;
    }

    public static string GpuName() => Nvml.Name();

    // ===== 网络 =====

    public static (bool Connected, string Name) Wifi() {
        try {
            var wireless = NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                .ToList();

            if (wireless.Count == 0)
                return (false, "无无线网卡");

            var up = wireless.FirstOrDefault(nic => nic.OperationalStatus == OperationalStatus.Up);

            return up != null
                ? (true, NicLabel(up))
                : (false, "未连接");
        }
        catch {
            return (false, "不可用");
        }
    }

    private static string NicLabel(NetworkInterface nic) {
        string name = nic.Name;

        // "WLAN" / "Wi-Fi" 这类名字信息量太少，补一个连接名
        return string.IsNullOrWhiteSpace(name) ? "已连接" : name;
    }

    // ===== 蓝牙 =====

    /// <summary>蓝牙：读注册表里的已配对设备数量（无需额外权限）。</summary>
    public static (bool Available, int Paired) Bluetooth() {
        try {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\BTHPORT\Parameters\Devices");

            if (key == null)
                return (false, 0);

            return (true, key.SubKeyCount);
        }
        catch {
            return (false, 0);
        }
    }

    // ===== 最近文件 =====

    private static List<(string Name, string Path)> _recent = new();
    private static long _recentStamp;

    /// <summary>系统「最近使用」目录里的快捷方式，按修改时间倒序。</summary>
    public static List<(string Name, string Path)> RecentFiles() {
        long now = Environment.TickCount64;
        if (now - _recentStamp < 5000 && _recent.Count > 0)
            return _recent;

        _recentStamp = now;

        try {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Windows\Recent");

            if (!Directory.Exists(dir))
                return _recent;

            _recent = new DirectoryInfo(dir)
                .EnumerateFiles("*.lnk")
                .OrderByDescending(file => file.LastWriteTime)
                .Take(8)
                .Select(file => (Path.GetFileNameWithoutExtension(file.Name), file.FullName))
                .ToList();
        }
        catch {
            // 忽略：读不到就空着
        }

        return _recent;
    }
}
