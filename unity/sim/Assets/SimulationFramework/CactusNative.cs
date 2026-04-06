using System;
using System.Runtime.InteropServices;
using System.Text;

namespace GemmaHackathon.SimulationFramework
{
    internal static class CactusNative
    {
        private const string LibraryName = "cactus";

        internal const int DefaultJsonBufferSize = 64 * 1024;
        internal const int LargeJsonBufferSize = 1024 * 1024;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void TokenCallback(IntPtr token, uint tokenId, IntPtr userData);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void LogCallback(int level, IntPtr component, IntPtr message, IntPtr userData);

        [DllImport(LibraryName, EntryPoint = "cactus_init", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        internal static extern IntPtr Init(string modelPath, string corpusDir, [MarshalAs(UnmanagedType.I1)] bool cacheIndex);

        [DllImport(LibraryName, EntryPoint = "cactus_destroy", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void Destroy(IntPtr model);

        [DllImport(LibraryName, EntryPoint = "cactus_reset", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void Reset(IntPtr model);

        [DllImport(LibraryName, EntryPoint = "cactus_stop", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void Stop(IntPtr model);

        [DllImport(LibraryName, EntryPoint = "cactus_complete", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        internal static extern int Complete(
            IntPtr model,
            string messagesJson,
            [Out] byte[] responseBuffer,
            UIntPtr bufferSize,
            string optionsJson,
            string toolsJson,
            TokenCallback callback,
            IntPtr userData,
            byte[] pcmBuffer,
            UIntPtr pcmBufferSize);

        [DllImport(LibraryName, EntryPoint = "cactus_prefill", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        internal static extern int Prefill(
            IntPtr model,
            string messagesJson,
            [Out] byte[] responseBuffer,
            UIntPtr bufferSize,
            string optionsJson,
            string toolsJson,
            byte[] pcmBuffer,
            UIntPtr pcmBufferSize);

        [DllImport(LibraryName, EntryPoint = "cactus_transcribe", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        internal static extern int Transcribe(
            IntPtr model,
            string audioFilePath,
            string prompt,
            [Out] byte[] responseBuffer,
            UIntPtr bufferSize,
            string optionsJson,
            TokenCallback callback,
            IntPtr userData,
            byte[] pcmBuffer,
            UIntPtr pcmBufferSize);

        [DllImport(LibraryName, EntryPoint = "cactus_stream_transcribe_start", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        internal static extern IntPtr StreamTranscribeStart(IntPtr model, string optionsJson);

        [DllImport(LibraryName, EntryPoint = "cactus_stream_transcribe_process", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int StreamTranscribeProcess(
            IntPtr stream,
            byte[] pcmBuffer,
            UIntPtr pcmBufferSize,
            [Out] byte[] responseBuffer,
            UIntPtr bufferSize);

        [DllImport(LibraryName, EntryPoint = "cactus_stream_transcribe_stop", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int StreamTranscribeStop(
            IntPtr stream,
            [Out] byte[] responseBuffer,
            UIntPtr bufferSize);

        [DllImport(LibraryName, EntryPoint = "cactus_set_telemetry_environment", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        internal static extern void SetTelemetryEnvironment(string framework, string cacheLocation, string version);

        [DllImport(LibraryName, EntryPoint = "cactus_set_app_id", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        internal static extern void SetAppId(string appId);

        [DllImport(LibraryName, EntryPoint = "cactus_telemetry_flush", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void TelemetryFlush();

        [DllImport(LibraryName, EntryPoint = "cactus_telemetry_shutdown", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void TelemetryShutdown();

        [DllImport(LibraryName, EntryPoint = "cactus_log_set_level", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void SetLogLevel(int level);

        [DllImport(LibraryName, EntryPoint = "cactus_log_set_callback", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void SetLogCallback(LogCallback callback, IntPtr userData);

        [DllImport(LibraryName, EntryPoint = "cactus_get_last_error", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr GetLastErrorPtr();

        internal static string GetLastError()
        {
            return PtrToString(GetLastErrorPtr());
        }

        internal static string PtrToString(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero)
            {
                return string.Empty;
            }

            return Marshal.PtrToStringAnsi(ptr) ?? string.Empty;
        }

        internal static string DecodeNullTerminatedUtf8(byte[] buffer)
        {
            if (buffer == null || buffer.Length == 0)
            {
                return string.Empty;
            }

            var length = Array.IndexOf(buffer, (byte)0);
            if (length < 0)
            {
                length = buffer.Length;
            }

            return Encoding.UTF8.GetString(buffer, 0, length);
        }

        internal static UIntPtr ToSizeT(int value)
        {
            return new UIntPtr(unchecked((uint)value));
        }
    }
}
