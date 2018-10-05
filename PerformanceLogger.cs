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
        public static bool openLogFolder = true;

        /// <summary>Current active logger.</summary>
        private static PerformanceLogger currentLogger;

        /// <summary>If the PerformanceLogger is currently logging.</summary>
        public static bool IsLogging { get { return currentLogger != null; } }

        /// <summary>The current state of the PerformanceLogger.</summary>
        public static LoggerState CurrentState { get; private set; }

        private readonly Dictionary<float, float> frameTimes = new Dictionary<float, float>();
        private readonly List<string> loggedCustomEvents = new List<string>();
        private readonly List<float> loggedCustomEventsTimestamps = new List<float>();

        private float startTime;
        private Action completionCallback;
        private string systemSpecs;

        /// <summary>Begins logging.</summary>
        public static void StartLogger()
        {
            if (currentLogger != null) { Destroy(currentLogger); }
            GameObject loggerObject = new GameObject("Performance Logger");
            currentLogger = loggerObject.AddComponent<PerformanceLogger>();
            CurrentState = LoggerState.Logging;
        }

        /// <summary>Adds a custom event to the performance logger.</summary>
        /// <param name="eventData">Event data.</param>
        public static void LogCustomEvent(string eventData)
        {
            if (currentLogger == null) { Debug.LogError("ERROR: No logger was running"); }
            else
            {
                currentLogger.loggedCustomEventsTimestamps.Add(Time.unscaledTime);
                currentLogger.loggedCustomEvents.Add(eventData);
            }
        }

        /// <summary>Ends the logger, dumping to a logfile.</summary>
        /// <param name="path">Full name and path of the logfile to dump to.</param>
        /// <param name="extraInfo">Any extra information to prepend to the logfile.</param>
        /// <param name="async">If the dump process should run in asynchronous mode.</param>
        /// <param name="completionCallback">An optional callback to execute upon completing the log dump.</param>
        public static void EndLogger(string path, string extraInfo = "", bool async = true, Action completionCallback = null)
        {
            if (currentLogger == null) { Debug.LogError("ERROR: No logger was running"); }
            else
            {
                CurrentState = LoggerState.Dumping;
                if (async)
                {
                    //Ends logger, begins asynchronous dump, completing all main thread only tasks before entering async mode
                    currentLogger.completionCallback = completionCallback;
                    if (openLogFolder)
                    {
                        if (completionCallback == null) { currentLogger.completionCallback = () => ShowLogFolder(path); }
                        else { currentLogger.completionCallback += () => ShowLogFolder(path); }
                    }
                    currentLogger.GetSystemSpecs();
                    Thread dumpThread = new Thread(new ThreadStart(() => currentLogger.DumpLog(path, extraInfo)));
                    dumpThread.IsBackground = true;
                    dumpThread.Start();
                }
                else
                {
                    //Dumps logfile and ends logger
                    currentLogger.DumpLog(path, extraInfo);
                    completionCallback();
                    if (openLogFolder) { ShowLogFolder(path); }
                    Destroy(currentLogger.gameObject);
                    currentLogger = null;
                }
            }
        }

        /// <summary>Opens the log folder for the user.</summary>
        /// <param name="path">Full name and path of the logfile to dump to.</param>
        private static void ShowLogFolder(string path)
        {
            System.Diagnostics.Process showProcess = new System.Diagnostics.Process();
#if UNITY_STANDALONE_WIN
            string command = "/select, \"" + path.Replace(@"/", @"\") + "\"";
            showProcess.StartInfo = new System.Diagnostics.ProcessStartInfo("explorer.exe", command);
#elif UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX
            string command = path.Replace(@"\", @"/");
            command = command.Trim();
            command = "open -R " + command.Replace(" ", @"\ ");
            command = command.Replace("'", @"\'");
            showProcess.StartInfo.FileName = "/bin/bash";
            showProcess.StartInfo.Arguments = "-c \" " + command + " \"";
            showProcess.StartInfo.UseShellExecute = false;
            showProcess.StartInfo.RedirectStandardOutput = true;
#endif
            showProcess.Start();
        }

        private void Start() { startTime = Time.realtimeSinceStartup; }

        private void Update()
        {
            if (currentLogger == null) { Destroy(this); }
            if (CurrentState == LoggerState.Logging) { LogFrameTime(); }
            else if (CurrentState == LoggerState.None)
            {
                //Terminates logger once dump is complete
                if (completionCallback != null) { completionCallback(); }
                currentLogger = null;
                Destroy(this.gameObject);
            }
        }

        private void LogFrameTime()
        {
            frameTimes.Add(Time.realtimeSinceStartup - startTime, Time.unscaledDeltaTime * 1000f);
        }

        private string AnalyseFramesUnderFPS(float FPSCutoff)
        {
            //Gets frames under threshold
            int frameCount = frameTimes.Count((KeyValuePair<float, float> x) => 1000f / x.Value < FPSCutoff);
            float totalFrameTime = frameTimes.Sum((KeyValuePair<float, float> x) => 1000f / x.Value < FPSCutoff ? x.Value : 0f);

            //Creates analysis string
#if NET_4_6
            string analysisString = $"FPS < {FPSCutoff}: {frameCount} frame{(frameCount == 1 ? "" : "s")} ({(100 * frameCount / (float)frameTimes.Count).RoundToSigFigs(3)}%)";
            analysisString += $", {(totalFrameTime / 1000).RoundToSigFigs(4)}s ({(0.1f * totalFrameTime / frameTimes.Keys.Max()).RoundToSigFigs(3)}%)";
#else
            string analysisString = "FPS < " + FPSCutoff.ToString() + ": " + frameCount.ToString() + " frame" + (frameCount == 1 ? "" : "s") + "(" + (100 * frameCount / (float)frameTimes.Count).RoundToSigFigs(3).ToString() + "%)";
            analysisString += ", " + (totalFrameTime / 1000).RoundToSigFigs(4).ToString() + "s (" + (0.1f * totalFrameTime / frameTimes.Keys.Max()).RoundToSigFigs(3) + "%)";
#endif
            return analysisString;
        }

        private void DumpLog(string path, string extraInfo = "")
        {
            //Analyses collected data
            float logDuration = frameTimes.Keys.Max();
            float averageFrameTime = frameTimes.Values.Average();
            float RMSFrameTime = Mathf.Sqrt(frameTimes.Sum((KeyValuePair<float, float> x) => Mathf.Pow(x.Value, 2) / frameTimes.Count));
            float minFrameTime = frameTimes.Values.Min();
            float maxFrameTime = frameTimes.Values.Max();
            float averageFPS = 1000 / averageFrameTime;
            float RMSFPS = 1000 / RMSFrameTime;
            float maxFPS = 1000 / minFrameTime;
            float minFPS = 1000 / maxFrameTime;
            float[] orderedFrameTimes = frameTimes.Values.OrderBy((float x) => x).ToArray();
            float p10FrameTime = orderedFrameTimes[Mathf.Max(0, Mathf.RoundToInt(frameTimes.Count * 0.1f) - 1)];
            float p90FrameTime = orderedFrameTimes[Mathf.RoundToInt(frameTimes.Count * 0.9f) - 1];
            float p10FPS = 1000 / p10FrameTime;
            float p90FPS = 1000 / p90FrameTime;

            //Adds analysis to logfile
            string dataString = extraInfo;
#if NET_4_6
            dataString += $"\n\n\nLog duration: {logDuration}s";
            dataString += $"\nTotal frames: {frameTimes.Count}";
            dataString += $"\n\nAverage frametime: {averageFrameTime.RoundToSigFigs(4)}ms, {averageFPS.RoundToSigFigs(4)} FPS";
            dataString += $"\nRMS frametime: {RMSFrameTime.RoundToSigFigs(4)}ms, {RMSFPS.RoundToSigFigs(4)} FPS";
            dataString += $"\nMinimum frametime: {minFrameTime.RoundToSigFigs(4)}ms, {maxFPS.RoundToSigFigs(4)} FPS";
            dataString += $"\nMaximum frametime: {maxFrameTime.RoundToSigFigs(4)}ms, {minFPS.RoundToSigFigs(4)} FPS";
            dataString += $"\np10%: {p10FrameTime.RoundToSigFigs(4)}ms, {p10FPS.RoundToSigFigs(4)} FPS";
            dataString += $"\np90%: {p90FrameTime.RoundToSigFigs(4)}ms, {p90FPS.RoundToSigFigs(4)} FPS";
            dataString += $"\n\n{AnalyseFramesUnderFPS(120)}";
            dataString += $"\n{AnalyseFramesUnderFPS(60)}";
            dataString += $"\n{AnalyseFramesUnderFPS(30)}";
            dataString += $"\n{AnalyseFramesUnderFPS(15)}";
            dataString += $"\n{AnalyseFramesUnderFPS(5)}";
            dataString += $"\n{AnalyseFramesUnderFPS(1)}";
#else
            dataString += "\n\n\nLog duration: " + logDuration.ToString() + "s";
            dataString += "\nTotal frames: " + frameTimes.Count.ToString();
            dataString += "\n\nAverage frametime: " + averageFrameTime.RoundToSigFigs(4).ToString() + "ms, " + averageFPS.RoundToSigFigs(4).ToString() + " FPS";
            dataString += "\nRMS frametime: " + RMSFrameTime.RoundToSigFigs(4).ToString() + "ms, " + RMSFPS.RoundToSigFigs(4).ToString() + " FPS";
            dataString += "\nMinimum frametime: " + minFrameTime.RoundToSigFigs(4).ToString() + "ms, " + maxFPS.RoundToSigFigs(4).ToString() + " FPS";
            dataString += "\nMaximum frametime: " + maxFrameTime.RoundToSigFigs(4).ToString() + "ms, " + minFPS.RoundToSigFigs(4).ToString() + " FPS";
            dataString += "\np10%: " + p10FrameTime.RoundToSigFigs(4).ToString() + "ms, " + p10FPS.RoundToSigFigs(4).ToString() + " FPS";
            dataString += "\np90%: " + p90FrameTime.RoundToSigFigs(4).ToString() + "ms, " + p90FPS.RoundToSigFigs(4).ToString() + " FPS";
            dataString += "\n\n" + AnalyseFramesUnderFPS(120);
            dataString += "\n" + AnalyseFramesUnderFPS(60);
            dataString += "\n" + AnalyseFramesUnderFPS(30);
            dataString += "\n" + AnalyseFramesUnderFPS(15);
            dataString += "\n" + AnalyseFramesUnderFPS(5);
            dataString += "\n" + AnalyseFramesUnderFPS(1);
#endif

            //Adds system specs to logfile
            if (string.IsNullOrEmpty(systemSpecs)) { GetSystemSpecs(); }
            dataString += "\n\n\n" + systemSpecs;

            //Writes data to file
            string directoryPath = System.IO.Path.GetDirectoryName(path);
            if (!Directory.Exists(directoryPath)) { Directory.CreateDirectory(directoryPath); }
            StreamWriter logFile = new StreamWriter(path);
            logFile.Write(dataString);

            //Writes custom events
            if (loggedCustomEvents.Count > 0)
            {
                logFile.Write("\n\n\n\nCustom events:");
                for (int i = 0; i < loggedCustomEvents.Count; i++)
                {
#if NET_4_6
                    logFile.Write($"\n{loggedCustomEventsTimestamps[i]}, {loggedCustomEvents[i]}");
#else
                    logFile.Write("\n" + loggedCustomEventsTimestamps[i].ToString() + ", " + loggedCustomEvents[i].ToString());
#endif
                }
            }

            //Writes raw data to the log file
            logFile.Write("\n\n\nFrametimes:");
            foreach (KeyValuePair<float, float> frame in frameTimes)
            {
#if NET_4_6
                logFile.Write($"\n{frame.Key}, {frame.Value}");
#else
                logFile.Write("\n" + frame.Key.ToString() + ", " + frame.Value.ToString());
#endif
            }

            //Closes file and ends
            logFile.Flush();
            logFile.Close();
            logFile.Dispose();
            CurrentState = LoggerState.None;
        }

        private string GetSystemSpecs()
        {
            systemSpecs = "System Specifications:";
#if NET_4_6
            systemSpecs += $"\n\n{SystemInfo.deviceName}";
            systemSpecs += $"\n{SystemInfo.deviceModel}";
            systemSpecs += $"\n{SystemInfo.deviceType}";
            systemSpecs += $"\n{Screen.width} x {Screen.height} @{Screen.currentResolution.refreshRate}Hz - {(Screen.fullScreen ? "Fullscreen" : "Windowed")}";
            systemSpecs += $"\n\nCPU: {SystemInfo.processorType}, {SystemInfo.processorCount}C, {SystemInfo.processorFrequency}MHz";
            systemSpecs += $"\nRAM: {SystemInfo.systemMemorySize}MB";
            systemSpecs += $"\nGPU: {SystemInfo.graphicsDeviceName}, {(SystemInfo.graphicsMultiThreaded ? "Multithreaded" : "Singlethreaded")}";
            systemSpecs += $"\nGraphics API: {SystemInfo.graphicsDeviceType}, {SystemInfo.graphicsDeviceVersion}";
            systemSpecs += $"\nVRAM: {SystemInfo.graphicsMemorySize}MB";
#else
            systemSpecs += "\n\n" + SystemInfo.deviceName;
            systemSpecs += "\n" + SystemInfo.deviceModel;
            systemSpecs += "\n" + SystemInfo.deviceType;
            systemSpecs += "\n" + Screen.width.ToString() + " x " + Screen.height.ToString() +" @" + Screen.currentResolution.refreshRate.ToString() + "Hz - " + (Screen.fullScreen ? "Fullscreen" : "Windowed");
            systemSpecs += "\n\nCPU: " + SystemInfo.processorType + ", " + SystemInfo.processorCount.ToString() + "C, " + SystemInfo.processorFrequency.ToString() + "MHz";
            systemSpecs += "\nRAM: " + SystemInfo.systemMemorySize.ToString() + "MB";
            systemSpecs += "\nGPU: " + SystemInfo.graphicsDeviceName + ", " + (SystemInfo.graphicsMultiThreaded ? "Multithreaded" : "Singlethreaded");
            systemSpecs += "\nGraphics API: " + SystemInfo.graphicsDeviceType + ", " + SystemInfo.graphicsDeviceVersion;
            systemSpecs += "\nVRAM: " + SystemInfo.graphicsMemorySize + "MB";
#endif
            return systemSpecs;
        }
    }

    namespace Internal
    {
        /// <summary>Extends the float class.</summary>
        public static class FloatExtension
        {
            /// <summary>Rounds the floating point value to n significant figures.</summary>
            /// <param name="n">The number of signiciant figures.</param>
            /// <param name="num">Rounded float.</param>
            public static float RoundToSigFigs(this float num, int n)
            {
                if (num == 0) { return num; }
                int currentSigFigs = (int)Math.Log10(Math.Abs(num)) + 1;
                if (n < 1) { return num; }

                //Reduces sig figs
                num /= (float)Math.Pow(10, currentSigFigs - n);
                num = (int)Math.Round(num);
                num *= (float)Math.Pow(10, currentSigFigs - n);

                return num;
            }
        }
    }
}
