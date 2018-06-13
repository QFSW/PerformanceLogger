using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;
using System.IO;
using System.Threading;
using QFSW.PL.Internal;

namespace QFSW.PL
{
    /// <summary>Logs performance on a frame by frame basis, analyses, and then dumps to a logfile.</summary>
    public class PerformanceLogger : MonoBehaviour
    {
        /// <summary>The possible different states of the PerformanceLogger.</summary>
        public enum LoggerState
        {
            None = 0,
            Logging = 1,
            Dumping = 2,
        }

        //If the folder containing the log should be opened at the end
        public static bool OpenLogFolder = true;

        /// <summary>Current active logger.</summary>
        private static PerformanceLogger CurrentLogger;

        /// <summary>If the PerformanceLogger is currently logging.</summary>
        public static bool IsLogging { get { return CurrentLogger != null; } }

        /// <summary>The current state of the PerformanceLogger.</summary>
        public static LoggerState CurrentState { get; private set; }

        /// <summary>All logged frametimes.</summary>
        private readonly Dictionary<float, float> FrameTimes = new Dictionary<float, float>();

        /// <summary>All logged custom events.</summary>
        private readonly List<string> LoggedCustomEvents = new List<string>();

        /// <summary>Timestamps for all logged custom events.</summary>
        private readonly List<float> LoggedCustomEventsTimestamps = new List<float>();

        /// <summary>Timestamp of when the logger began logging.</summary>
        private float StartTime;

        /// <summary>Callback to execute upon dump completion.</summary>
        private Action CompletionCallback;

        /// <summary>Formatted string containing full system specifications.</summary>
        private string SystemSpecs;

        /// <summary>Begins logging.</summary>
        public static void StartLogger()
        {
            if (CurrentLogger != null) { Destroy(CurrentLogger); }
            GameObject LoggerObject = new GameObject("Performance Logger");
            CurrentLogger = LoggerObject.AddComponent<PerformanceLogger>();
            CurrentState = LoggerState.Logging;
        }

        /// <summary>Adds a custom event to the performance logger.</summary>
        /// <param name="EventData">Event data.</param>
        public static void LogCustomEvent(string EventData)
        {
            if (CurrentLogger == null) { Debug.LogError("ERROR: No logger was running"); }
            else
            {
                CurrentLogger.LoggedCustomEventsTimestamps.Add(Time.unscaledTime);
                CurrentLogger.LoggedCustomEvents.Add(EventData);
            }
        }

        /// <summary>Ends the logger, dumping to a logfile.</summary>
        /// <param name="Path">Full name and path of the logfile to dump to.</param>
        /// <param name="ExtraInfo">Any extra information to prepend to the logfile.</param>
        /// <param name="Async">If the dump process should run in asynchronous mode.</param>
        /// <param name="CompletionCallback">An optional callback to execute upon completing the log dump.</param>
        public static void EndLogger(string Path, string ExtraInfo = "", bool Async = true, Action CompletionCallback = null)
        {
            if (CurrentLogger == null) { Debug.LogError("ERROR: No logger was running"); }
            else
            {
                CurrentState = LoggerState.Dumping;
                if (Async)
                {
                    //Ends logger, begins asynchronous dump, completing all main thread only tasks before entering async mode
                    CurrentLogger.CompletionCallback = CompletionCallback;
                    if (OpenLogFolder)
                    {
                        if (CompletionCallback == null) { CurrentLogger.CompletionCallback = () => ShowLogFolder(Path); }
                        else { CurrentLogger.CompletionCallback += () => ShowLogFolder(Path); }
                    }
                    CurrentLogger.GetSystemSpecs();
                    Thread DumpThread = new Thread(new ThreadStart(() => CurrentLogger.DumpLog(Path, ExtraInfo)));
                    DumpThread.IsBackground = true;
                    DumpThread.Start();
                }
                else
                {
                    //Dumps logfile and ends logger
                    CurrentLogger.DumpLog(Path, ExtraInfo);
                    CompletionCallback();
                    if (OpenLogFolder) { ShowLogFolder(Path); }
                    Destroy(CurrentLogger.gameObject);
                    CurrentLogger = null;
                }
            }
        }

        /// <summary>Opens the log folder for the user.</summary>
        /// <param name="Path">Full name and path of the logfile to dump to.</param>
        private static void ShowLogFolder(string Path)
        {
            System.Diagnostics.Process ShowProcess = new System.Diagnostics.Process();
#if UNITY_STANDALONE_WIN
            string Command = "/select, \"" + Path.Replace(@"/", @"\") + "\"";
            ShowProcess.StartInfo = new System.Diagnostics.ProcessStartInfo("explorer.exe", Command);
#elif UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX
        string Command = Path.Replace(@"\", @"/");
        Command = Command.Trim();
        Command = "open -R " + Command.Replace(" ", @"\ ");
        Command = Command.Replace("'", @"\'");
        ShowProcess.StartInfo.FileName = "/bin/bash";
        ShowProcess.StartInfo.Arguments = "-c \" " + Command + " \"";
        ShowProcess.StartInfo.UseShellExecute = false;
        ShowProcess.StartInfo.RedirectStandardOutput = true;
#endif
            ShowProcess.Start();
        }

        //Initialisation
        private void Start() { StartTime = Time.realtimeSinceStartup; }

        private void Update()
        {
            if (CurrentLogger == null) { Destroy(this); }
            if (CurrentState == LoggerState.Logging) { LogFrameTime(); }
            else if (CurrentState == LoggerState.None)
            {
                //Terminates logger once dump is complete
                if (CompletionCallback != null) { CompletionCallback(); }
                CurrentLogger = null;
                Destroy(this.gameObject);
            }
        }

        /// <summary>Logs the current frame.</summary>
        private void LogFrameTime()
        {
            FrameTimes.Add(Time.realtimeSinceStartup - StartTime, Time.unscaledDeltaTime * 1000f);
        }

        /// <summary>Analyses frame count of frames that fell under a specified FPS.</summary>
        /// <returns>Analysis string.</returns>
        /// <param name="FPSCutoff">Inclusive maximum FPS for frames included in analysis.</param>
        private string AnalyseFramesUnderFPS(float FPSCutoff)
        {
            //Gets frames under threshold
            int FrameCount = FrameTimes.Count((KeyValuePair<float, float> x) => 1000f / x.Value < FPSCutoff);
            float TotalFrameTime = FrameTimes.Sum((KeyValuePair<float, float> x) => 1000f / x.Value < FPSCutoff ? x.Value : 0f);

            //Creates analysis string
#if NET_4_6
            string AnalysisString = $"FPS < {FPSCutoff}: {FrameCount} frame{(FrameCount == 1 ? "" : "s")} ({(100 * FrameCount / (float)FrameTimes.Count).RoundToSigFigs(3)}%)";
            AnalysisString += $", {(TotalFrameTime / 1000).RoundToSigFigs(4)}s ({(0.1f * TotalFrameTime / FrameTimes.Keys.Max()).RoundToSigFigs(3)}%)";
#else
            string AnalysisString = "FPS < " + FPSCutoff.ToString() + ": " + FrameCount.ToString() + " frame" + (FrameCount == 1 ? "" : "s") + "(" + (100 * FrameCount / (float)FrameTimes.Count).RoundToSigFigs(3).ToString() + "%)";
            AnalysisString += ", " + (TotalFrameTime / 1000).RoundToSigFigs(4).ToString() + "s (" + (0.1f * TotalFrameTime / FrameTimes.Keys.Max()).RoundToSigFigs(3) + "%)";
#endif
            return AnalysisString;
        }

        /// <summary>Dumps the log to a logfile.</summary>
        /// <param name="Path">Full name and path of the logfile to dump to.</param>
        /// <param name="ExtraInfo">Any extra information to prepend to the logfile.</param>
        private void DumpLog(string Path, string ExtraInfo = "")
        {
            //Analyses collected data
            float LogDuration = FrameTimes.Keys.Max();
            float AverageFrameTime = FrameTimes.Values.Average();
            float RMSFrameTime = Mathf.Sqrt(FrameTimes.Sum((KeyValuePair<float, float> x) => Mathf.Pow(x.Value, 2) / FrameTimes.Count));
            float MinFrameTime = FrameTimes.Values.Min();
            float MaxFrameTime = FrameTimes.Values.Max();
            float AverageFPS = 1000 / AverageFrameTime;
            float RMSFPS = 1000 / RMSFrameTime;
            float MaxFPS = 1000 / MinFrameTime;
            float MinFPS = 1000 / MaxFrameTime;
            float[] OrderedFrameTimes = FrameTimes.Values.OrderBy((float x) => x).ToArray();
            float p10FrameTime = OrderedFrameTimes[Mathf.Max(0, Mathf.RoundToInt(FrameTimes.Count * 0.1f) - 1)];
            float p90FrameTime = OrderedFrameTimes[Mathf.RoundToInt(FrameTimes.Count * 0.9f) - 1];
            float p10FPS = 1000 / p10FrameTime;
            float p90FPS = 1000 / p90FrameTime;

            //Adds analysis to logfile
            string DataString = ExtraInfo;
#if NET_4_6
            DataString += $"\n\n\nLog duration: {LogDuration}s";
            DataString += $"\nTotal frames: {FrameTimes.Count}";
            DataString += $"\n\nAverage frametime: {AverageFrameTime.RoundToSigFigs(4)}ms, {AverageFPS.RoundToSigFigs(4)} FPS";
            DataString += $"\nRMS frametime: {RMSFrameTime.RoundToSigFigs(4)}ms, {RMSFPS.RoundToSigFigs(4)} FPS";
            DataString += $"\nMinimum frametime: {MinFrameTime.RoundToSigFigs(4)}ms, {MaxFPS.RoundToSigFigs(4)} FPS";
            DataString += $"\nMaximum frametime: {MaxFrameTime.RoundToSigFigs(4)}ms, {MinFPS.RoundToSigFigs(4)} FPS";
            DataString += $"\np10%: {p10FrameTime.RoundToSigFigs(4)}ms, {p10FPS.RoundToSigFigs(4)} FPS";
            DataString += $"\np90%: {p90FrameTime.RoundToSigFigs(4)}ms, {p90FPS.RoundToSigFigs(4)} FPS";
            DataString += $"\n\n{AnalyseFramesUnderFPS(120)}";
            DataString += $"\n{AnalyseFramesUnderFPS(60)}";
            DataString += $"\n{AnalyseFramesUnderFPS(30)}";
            DataString += $"\n{AnalyseFramesUnderFPS(15)}";
            DataString += $"\n{AnalyseFramesUnderFPS(5)}";
            DataString += $"\n{AnalyseFramesUnderFPS(1)}";
#else
            DataString += "\n\n\nLog duration: " + LogDuration.ToString() + "s";
            DataString += "\nTotal frames: " + FrameTimes.Count.ToString();
            DataString += "\n\nAverage frametime: " + AverageFrameTime.RoundToSigFigs(4).ToString() + "ms, " + AverageFPS.RoundToSigFigs(4).ToString() + " FPS";
            DataString += "\nRMS frametime: " + RMSFrameTime.RoundToSigFigs(4).ToString() + "ms, " + RMSFPS.RoundToSigFigs(4).ToString() + " FPS";
            DataString += "\nMinimum frametime: " + MinFrameTime.RoundToSigFigs(4).ToString() + "ms, " + MaxFPS.RoundToSigFigs(4).ToString() + " FPS";
            DataString += "\nMaximum frametime: " + MaxFrameTime.RoundToSigFigs(4).ToString() + "ms, " + MinFPS.RoundToSigFigs(4).ToString() + " FPS";
            DataString += "\np10%: " + p10FrameTime.RoundToSigFigs(4).ToString() + "ms, " + p10FPS.RoundToSigFigs(4).ToString() + " FPS";
            DataString += "\np90%: " + p90FrameTime.RoundToSigFigs(4).ToString() + "ms, " + p90FPS.RoundToSigFigs(4).ToString() + " FPS";
            DataString += "\n\n" + AnalyseFramesUnderFPS(120);
            DataString += "\n" + AnalyseFramesUnderFPS(60);
            DataString += "\n" + AnalyseFramesUnderFPS(30);
            DataString += "\n" + AnalyseFramesUnderFPS(15);
            DataString += "\n" + AnalyseFramesUnderFPS(5);
            DataString += "\n" + AnalyseFramesUnderFPS(1);
#endif

            //Adds system specs to logfile
            if (string.IsNullOrEmpty(SystemSpecs)) { GetSystemSpecs(); }
            DataString += "\n\n\n" + SystemSpecs;

            //Writes data to file
            string DirectoryPath = System.IO.Path.GetDirectoryName(Path);
            if (!Directory.Exists(DirectoryPath)) { Directory.CreateDirectory(DirectoryPath); }
            StreamWriter LogFile = new StreamWriter(Path);
            LogFile.Write(DataString);

            //Writes custom events
            if (LoggedCustomEvents.Count > 0)
            {
                LogFile.Write("\n\n\n\nCustom events:");
                for (int i = 0; i < LoggedCustomEvents.Count; i++)
                {
#if NET_4_6
                    LogFile.Write($"\n{LoggedCustomEventsTimestamps[i]}, {LoggedCustomEvents[i]}");
#else
                    LogFile.Write("\n" + LoggedCustomEventsTimestamps[i].ToString() + ", " + LoggedCustomEvents[i].ToString());
#endif
                }
            }

            //Writes raw data to the log file
            LogFile.Write("\n\n\nFrametimes:");
            foreach (KeyValuePair<float, float> Frame in FrameTimes)
            {
#if NET_4_6
                LogFile.Write($"\n{Frame.Key}, {Frame.Value}");
#else
                LogFile.Write("\n" + Frame.Key.ToString() + ", " + Frame.Value.ToString());
#endif
            }

            //Closes file and ends
            LogFile.Flush();
            LogFile.Close();
            LogFile.Dispose();
            CurrentState = LoggerState.None;
        }

        /// <summary>Gets and stores the system specs.</summary>
        /// <returns>A formatted string containing the full system specs.</returns>
        private string GetSystemSpecs()
        {
            SystemSpecs = "System Specifications:";
#if NET_4_6
            SystemSpecs += $"\n\n{SystemInfo.deviceName}";
            SystemSpecs += $"\n{SystemInfo.deviceModel}";
            SystemSpecs += $"\n{SystemInfo.deviceType}";
            SystemSpecs += $"\n{Screen.width} x {Screen.height} @{Screen.currentResolution.refreshRate}Hz - {(Screen.fullScreen ? "Fullscreen" : "Windowed")}";
            SystemSpecs += $"\n\nCPU: {SystemInfo.processorType}, {SystemInfo.processorCount}C, {SystemInfo.processorFrequency}MHz";
            SystemSpecs += $"\nRAM: {SystemInfo.systemMemorySize}MB";
            SystemSpecs += $"\nGPU: {SystemInfo.graphicsDeviceName}, {(SystemInfo.graphicsMultiThreaded ? "Multithreaded" : "Singlethreaded")}";
            SystemSpecs += $"\nGraphics API: {SystemInfo.graphicsDeviceType}, {SystemInfo.graphicsDeviceVersion}";
            SystemSpecs += $"\nVRAM: {SystemInfo.graphicsMemorySize}MB";
#else
            SystemSpecs += "\n\n" + SystemInfo.deviceName;
            SystemSpecs += "\n" + SystemInfo.deviceModel;
            SystemSpecs += "\n" + SystemInfo.deviceType;
            SystemSpecs += "\n" + Screen.width.ToString() + " x " + Screen.height.ToString() +" @" + Screen.currentResolution.refreshRate.ToString() + "Hz - " + (Screen.fullScreen ? "Fullscreen" : "Windowed");
            SystemSpecs += "\n\nCPU: " + SystemInfo.processorType + ", " + SystemInfo.processorCount.ToString() + "C, " + SystemInfo.processorFrequency.ToString() + "MHz";
            SystemSpecs += "\nRAM: " + SystemInfo.systemMemorySize.ToString() + "MB";
            SystemSpecs += "\nGPU: " + SystemInfo.graphicsDeviceName + ", " + (SystemInfo.graphicsMultiThreaded ? "Multithreaded" : "Singlethreaded");
            SystemSpecs += "\nGraphics API: " + SystemInfo.graphicsDeviceType + ", " + SystemInfo.graphicsDeviceVersion;
            SystemSpecs += "\nVRAM: " + SystemInfo.graphicsMemorySize + "MB";
#endif
            return SystemSpecs;
        }
    }

    namespace Internal
    {
        /// <summary>Extends the float class.</summary>
        public static class FloatExtension
        {
            /// <summary>Rounds the floating point value to n significant figures.</summary>
            /// <param name="n">The number of signiciant figures.</param>
            /// <param name="Num">Rounded float.</param>
            public static float RoundToSigFigs(this float Num, int n)
            {
                if (Num == 0) { return Num; }

                int CurrentSigFigs = (int)Math.Log10(Math.Abs(Num)) + 1;

                if (n < 1) { return Num; }

                //Reduces sig figs
                Num /= (float)Math.Pow(10, CurrentSigFigs - n);
                Num = (int)Math.Round(Num);
                Num *= (float)Math.Pow(10, CurrentSigFigs - n);

                return Num;
            }
        }
    }
}
