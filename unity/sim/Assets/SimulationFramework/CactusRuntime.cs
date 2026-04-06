using System;

namespace GemmaHackathon.SimulationFramework
{
    public enum CactusLogLevel
    {
        Debug = 0,
        Info = 1,
        Warn = 2,
        Error = 3,
        None = 4
    }

    public static class CactusRuntime
    {
        public const string AndroidPluginPath = "Assets/Plugins/Android/arm64-v8a/libcactus.so";

        private static Action<CactusLogLevel, string, string> s_logSink;
        private static CactusNative.LogCallback s_logCallback;

        public static void ConfigureTelemetry(string cacheLocation, string version = null)
        {
            InvokeNative(() => CactusNative.SetTelemetryEnvironment("unity", cacheLocation, version));
        }

        public static void SetAppId(string appId)
        {
            InvokeNative(() => CactusNative.SetAppId(appId));
        }

        public static void FlushTelemetry()
        {
            InvokeNative(CactusNative.TelemetryFlush);
        }

        public static void ShutdownTelemetry()
        {
            InvokeNative(CactusNative.TelemetryShutdown);
        }

        public static void SetLogLevel(CactusLogLevel level)
        {
            InvokeNative(() => CactusNative.SetLogLevel((int)level));
        }

        public static void SetLogCallback(Action<CactusLogLevel, string, string> callback)
        {
            s_logSink = callback;

            if (callback == null)
            {
                s_logCallback = null;
                InvokeNative(() => CactusNative.SetLogCallback(null, IntPtr.Zero));
                return;
            }

            s_logCallback = ForwardLogMessage;
            InvokeNative(() => CactusNative.SetLogCallback(s_logCallback, IntPtr.Zero));
        }

        internal static T InvokeNative<T>(Func<T> action)
        {
            try
            {
                return action();
            }
            catch (DllNotFoundException ex)
            {
                throw CreatePluginLoadException(ex);
            }
            catch (EntryPointNotFoundException ex)
            {
                throw new InvalidOperationException(
                    "Cactus native plugin is present but missing expected symbols. Rebuild `libcactus.so` from the vendored Cactus revision.",
                    ex);
            }
        }

        internal static void InvokeNative(Action action)
        {
            InvokeNative(() =>
            {
                action();
                return 0;
            });
        }

        internal static InvalidOperationException CreatePluginLoadException(Exception innerException)
        {
            return new InvalidOperationException(
                "Cactus native plugin could not be loaded. Build `libcactus.so` with `cactus build --android` and place it at `" +
                AndroidPluginPath +
                "`.",
                innerException);
        }

        private static void ForwardLogMessage(int level, IntPtr component, IntPtr message, IntPtr userData)
        {
            var sink = s_logSink;
            if (sink == null)
            {
                return;
            }

            sink(
                (CactusLogLevel)level,
                CactusNative.PtrToString(component),
                CactusNative.PtrToString(message));
        }
    }
}
