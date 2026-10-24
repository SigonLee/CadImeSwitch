using System;
using System.IO;
using System.Linq;
using System.Text;

namespace CadImeSwitch.Core
{
    /// <summary>
    /// 轻量日志。默认写入 %APPDATA%\CadImeSwitch\logs\CadImeSwitch-yyyyMMdd.log。
    /// 支持总开关、详细日志开关、单文件大小上限和文件数量上限，避免日志无限增长。
    /// </summary>
    public static class Logger
    {
        private const long MaxFileBytes = 2 * 1024 * 1024;   // 单文件 2 MB
        private const int MaxFiles = 8;                       // 最多保留 8 个文件
        private const long MaxTotalBytes = 12 * 1024 * 1024;  // 总计 12 MB

        private static readonly object Sync = new object();
        private static string _logDirectory;
        private static bool _verbose;
        private static bool _enabled = true;

        public static bool Enabled => _enabled;

        public static string LogDirectory
        {
            get
            {
                if (string.IsNullOrEmpty(_logDirectory))
                {
                    _logDirectory = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "CadImeSwitch", "logs");
                }
                return _logDirectory;
            }
        }

        public static void Initialize(bool verbose, bool enabled)
        {
            _verbose = verbose;
            _enabled = enabled;
            if (!_enabled) return;

            try { Directory.CreateDirectory(LogDirectory); } catch { }
            Info("============================================================");
            Info("CadImeSwitch 1.3.8 启动");
            Info($"AutoCAD 版本: {GetAcadVersion()}");
            Info($"进程: {Environment.ProcessPath}");
            Info($"64 位进程: {Environment.Is64BitProcess}");
            Info($"详细日志: {_verbose}");
            Info("============================================================");
            Cleanup();
        }

        public static void Info(string message)
        {
            if (_enabled) Write("INFO ", message);
        }

        public static void Warn(string message)
        {
            if (_enabled) Write("WARN ", message);
        }

        public static void Error(string message)
        {
            if (_enabled) Write("ERROR", message);
        }

        public static void Error(string message, Exception ex)
        {
            if (_enabled) Write("ERROR", message + " : " + ex);
        }

        public static void Verbose(string message)
        {
            if (_enabled && _verbose) Write("DEBUG", message);
        }

        private static string CurrentFile
            => Path.Combine(LogDirectory, $"CadImeSwitch-{DateTime.Now:yyyyMMdd}.log");

        private static void Write(string level, string message)
        {
            try
            {
                lock (Sync)
                {
                    Directory.CreateDirectory(LogDirectory);
                    RotateIfNeeded();
                    File.AppendAllText(CurrentFile,
                        $"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}",
                        Encoding.UTF8);
                }
            }
            catch
            {
                // 日志失败不能影响 CAD 主流程。
            }
        }

        private static void RotateIfNeeded()
        {
            try
            {
                var fi = new FileInfo(CurrentFile);
                if (!fi.Exists || fi.Length < MaxFileBytes) return;

                string rotated = Path.Combine(LogDirectory,
                    $"CadImeSwitch-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.log");
                fi.MoveTo(rotated);
                Cleanup();
            }
            catch { }
        }

        private static void Cleanup()
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                var files = new DirectoryInfo(LogDirectory)
                    .GetFiles("CadImeSwitch-*.log")
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .ToList();

                long total = 0;
                for (int i = 0; i < files.Count; i++)
                {
                    total += files[i].Length;
                    if (i >= MaxFiles || total > MaxTotalBytes)
                    {
                        try { files[i].Delete(); } catch { }
                    }
                }
            }
            catch { }
        }

        private static string GetAcadVersion()
        {
            try
            {
                return Autodesk.AutoCAD.ApplicationServices.Application.Version.ToString();
            }
            catch
            {
                return "未知";
            }
        }
    }
}
