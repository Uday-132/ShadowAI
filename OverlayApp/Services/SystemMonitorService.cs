using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows.Threading;

namespace OverlayApp.Services
{
    /// <summary>
    /// Service that collects real-time system performance metrics (CPU and RAM usage)
    /// using Win32 API calls instead of heavy performance counters, avoiding NuGet dependencies.
    /// </summary>
    public class SystemMonitorService
    {
        // Import GetSystemTimes to calculate CPU load
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

        // Struct to hold system memory status
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad; // Memory load in percentage
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
            
            public MEMORYSTATUSEX()
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        private FILETIME _prevIdleTime;
        private FILETIME _prevKernelTime;
        private FILETIME _prevUserTime;

        private readonly DispatcherTimer _timer;
        
        /// <summary>
        /// Fires when CPU or RAM metrics are updated. Arguments are: (CPU %, RAM %)
        /// </summary>
        public event Action<double, double>? MetricsUpdated;

        public SystemMonitorService()
        {
            // Initial call to set baseline for CPU calculation
            GetSystemTimes(out _prevIdleTime, out _prevKernelTime, out _prevUserTime);

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1.0);
            _timer.Tick += Timer_Tick;
        }

        /// <summary>
        /// Starts collecting metrics.
        /// </summary>
        public void Start()
        {
            _timer.Start();
        }

        /// <summary>
        /// Stops collecting metrics.
        /// </summary>
        public void Stop()
        {
            _timer.Stop();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            double cpu = CalculateCpuUsage();
            double ram = GetMemoryUsagePercentage();
            MetricsUpdated?.Invoke(cpu, ram);
        }

        /// <summary>
        /// Calculates the global CPU usage percentage by comparing system times.
        /// </summary>
        private double CalculateCpuUsage()
        {
            if (!GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime))
            {
                return 0.0;
            }

            ulong idleTimeCurr = ConvertFileTime(idleTime);
            ulong kernelTimeCurr = ConvertFileTime(kernelTime);
            ulong userTimeCurr = ConvertFileTime(userTime);

            ulong idleTimePrev = ConvertFileTime(_prevIdleTime);
            ulong kernelTimePrev = ConvertFileTime(_prevKernelTime);
            ulong userTimePrev = ConvertFileTime(_prevUserTime);

            ulong idleDelta = idleTimeCurr - idleTimePrev;
            ulong kernelDelta = kernelTimeCurr - kernelTimePrev;
            ulong userDelta = userTimeCurr - userTimePrev;

            ulong totalDelta = kernelDelta + userDelta;

            // Cache current times for the next calculation
            _prevIdleTime = idleTime;
            _prevKernelTime = kernelTime;
            _prevUserTime = userTime;

            if (totalDelta == 0) return 0.0;

            // Total busy time is (KernelTimeDelta + UserTimeDelta) - IdleTimeDelta
            // Since kernelDelta includes idle time, the total delta represents the total duration.
            // Formula: CPU% = (TotalDelta - IdleDelta) / TotalDelta
            double cpuUsage = (double)(totalDelta - idleDelta) / totalDelta;
            return Math.Max(0.0, Math.Min(100.0, cpuUsage * 100.0));
        }

        /// <summary>
        /// Retrieves the global memory load percentage.
        /// </summary>
        private double GetMemoryUsagePercentage()
        {
            var memoryStatus = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(memoryStatus))
            {
                return memoryStatus.dwMemoryLoad;
            }
            return 0.0;
        }

        private ulong ConvertFileTime(FILETIME fileTime)
        {
            return ((ulong)fileTime.dwHighDateTime << 32) | (uint)fileTime.dwLowDateTime;
        }
    }
}
