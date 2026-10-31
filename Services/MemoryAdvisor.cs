using System;
using System.Runtime.InteropServices;

namespace MyMCLauncher.Services;

/// <summary>
/// 根据物理内存给出默认内存建议，并校验用户设置。
/// </summary>
public sealed class MemoryAdvisor
{
    /// <summary>允许的最小内存（MB）。</summary>
    public const int MinimumMemoryMb = 256;

    /// <summary>允许的最大内存（MB）。</summary>
    public const int MaximumMemoryMb = 65536;

    /// <summary>自动建议的内存上限（MB）。</summary>
    public const int RecommendedMemoryCapMb = 8192;

    /// <summary>
    /// 获取物理内存总量（MB）；无法获取时返回 0。
    /// </summary>
    public int GetTotalPhysicalMemoryMb()
    {
        try
        {
            var status = new MemoryStatusEx
            {
                dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>(),
            };

            if (GlobalMemoryStatusEx(ref status))
            {
                return (int)Math.Min(status.ullTotalPhys / 1024 / 1024, int.MaxValue);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // 落到 GC 回退值
        }

        var totalAvailable = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        return totalAvailable > 0
            ? (int)Math.Min(totalAvailable / 1024 / 1024, int.MaxValue)
            : 0;
    }

    /// <summary>
    /// 获取建议内存（MB）。
    /// </summary>
    /// <remarks>
    /// 与 HMCL 一致：总内存 ≥ 32 GB 时固定 8192 MB，
    /// 否则取总内存的 1/4，按 128 MB 对齐，最低 256 MB。
    /// </remarks>
    public int GetRecommendedMemoryMb() => CalculateRecommendedMemoryMb(GetTotalPhysicalMemoryMb());

    /// <summary>
    /// 根据给定物理内存计算建议值，便于测试。
    /// </summary>
    public static int CalculateRecommendedMemoryMb(int totalPhysicalMemoryMb)
    {
        if (totalPhysicalMemoryMb <= 0)
        {
            return 2048;
        }

        if (totalPhysicalMemoryMb >= 32768)
        {
            return RecommendedMemoryCapMb;
        }

        var suggestion = (int)(
            Math.Round(totalPhysicalMemoryMb / 4.0 / 128.0, MidpointRounding.AwayFromZero) * 128);

        return Math.Clamp(suggestion, MinimumMemoryMb, RecommendedMemoryCapMb);
    }

    /// <summary>
    /// 将用户输入夹到合法范围；小于等于 0 时返回建议值。
    /// </summary>
    public int Clamp(int requestedMb)
    {
        if (requestedMb <= 0)
        {
            return GetRecommendedMemoryMb();
        }

        return Math.Clamp(requestedMb, MinimumMemoryMb, MaximumMemoryMb);
    }

    /// <summary>
    /// 校验内存设置，返回警告文本；<see langword="null"/> 表示正常。
    /// </summary>
    public string? Validate(int requestedMb)
    {
        var total = GetTotalPhysicalMemoryMb();

        if (requestedMb < 512)
        {
            return "内存低于 512 MB，现代版本可能无法启动。";
        }

        if (total > 0 && requestedMb > total / 2)
        {
            return $"已超过物理内存的 50%（总内存 {total} MB），可能导致系统卡顿。";
        }

        if (total > 0 && requestedMb > total - 1024)
        {
            return "接近物理内存上限，可能导致系统卡顿或分配失败。";
        }

        return null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);
}
