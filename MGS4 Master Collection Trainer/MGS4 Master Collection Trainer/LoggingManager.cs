using System;
using System.Diagnostics;
using System.IO;

namespace MGS4_Master_Collection_Trainer
{
    public sealed class LoggingManager
    {
        private static readonly Lazy<LoggingManager> instance =
            new Lazy<LoggingManager>(() => new LoggingManager());

        private readonly object logLock = new object();
        private readonly string logFolderPath;

        private LoggingManager()
        {
            logFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            LogPath = Path.Combine(logFolderPath, "MGS4 Trainer Log.txt");
        }

        public static LoggingManager Instance => instance.Value;

        public string LogPath { get; }

        /// <summary>
        /// Appends a timestamped message. Logging failures do not interrupt
        /// memory operations or require a user interface.
        /// </summary>
        public void Log(string message)
        {
            lock (logLock)
            {
                try
                {
                    Directory.CreateDirectory(logFolderPath);
                    using (var writer = new StreamWriter(LogPath, true))
                    {
                        writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}: {message}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Could not write trainer log: {ex.Message}");
                }
            }
        }
    }
}
