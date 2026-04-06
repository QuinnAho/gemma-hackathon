using System;

namespace GemmaHackathon.SimulationFramework
{
    public sealed class CactusModel : IDisposable
    {
        private IntPtr _handle;

        public CactusModel(string modelPath, string corpusDirectory = null, bool cacheIndex = true)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
            {
                throw new ArgumentException("Model path is required.", "modelPath");
            }

            _handle = CactusRuntime.InvokeNative(() => CactusNative.Init(modelPath, corpusDirectory, cacheIndex));
            if (_handle == IntPtr.Zero)
            {
                throw CreateOperationException("initialize model");
            }
        }

        public bool IsDisposed
        {
            get { return _handle == IntPtr.Zero; }
        }

        public void Reset()
        {
            EnsureNotDisposed();
            CactusRuntime.InvokeNative(() => CactusNative.Reset(_handle));
        }

        public void Stop()
        {
            EnsureNotDisposed();
            CactusRuntime.InvokeNative(() => CactusNative.Stop(_handle));
        }

        public string CompleteJson(
            string messagesJson,
            string optionsJson = null,
            string toolsJson = null,
            Action<string, uint> tokenCallback = null,
            byte[] pcm16Mono = null,
            int responseBufferBytes = CactusNative.DefaultJsonBufferSize)
        {
            EnsureNotDisposed();
            ValidateBufferSize(responseBufferBytes);

            var responseBuffer = new byte[responseBufferBytes];
            CactusNative.TokenCallback callbackBridge = null;

            if (tokenCallback != null)
            {
                callbackBridge = (token, tokenId, userData) =>
                {
                    tokenCallback(CactusNative.PtrToString(token), tokenId);
                };
            }

            var result = CactusRuntime.InvokeNative(() =>
                CactusNative.Complete(
                    _handle,
                    messagesJson ?? "[]",
                    responseBuffer,
                    CactusNative.ToSizeT(responseBuffer.Length),
                    optionsJson,
                    toolsJson,
                    callbackBridge,
                    IntPtr.Zero,
                    pcm16Mono,
                    CactusNative.ToSizeT(pcm16Mono == null ? 0 : pcm16Mono.Length)));

            GC.KeepAlive(callbackBridge);

            if (result < 0)
            {
                throw CreateOperationException("run completion");
            }

            return CactusNative.DecodeNullTerminatedUtf8(responseBuffer);
        }

        public string PrefillJson(
            string messagesJson,
            string optionsJson = null,
            string toolsJson = null,
            byte[] pcm16Mono = null,
            int responseBufferBytes = CactusNative.DefaultJsonBufferSize)
        {
            EnsureNotDisposed();
            ValidateBufferSize(responseBufferBytes);

            var responseBuffer = new byte[responseBufferBytes];
            var result = CactusRuntime.InvokeNative(() =>
                CactusNative.Prefill(
                    _handle,
                    messagesJson ?? "[]",
                    responseBuffer,
                    CactusNative.ToSizeT(responseBuffer.Length),
                    optionsJson,
                    toolsJson,
                    pcm16Mono,
                    CactusNative.ToSizeT(pcm16Mono == null ? 0 : pcm16Mono.Length)));

            if (result < 0)
            {
                throw CreateOperationException("prefill prompt");
            }

            return CactusNative.DecodeNullTerminatedUtf8(responseBuffer);
        }

        public string TranscribeFileJson(
            string audioFilePath,
            string prompt = null,
            string optionsJson = null,
            Action<string, uint> tokenCallback = null,
            int responseBufferBytes = CactusNative.DefaultJsonBufferSize)
        {
            if (string.IsNullOrWhiteSpace(audioFilePath))
            {
                throw new ArgumentException("Audio file path is required.", "audioFilePath");
            }

            return TranscribeInternal(audioFilePath, null, prompt, optionsJson, tokenCallback, responseBufferBytes);
        }

        public string TranscribePcmJson(
            byte[] pcm16Mono,
            string prompt = null,
            string optionsJson = null,
            Action<string, uint> tokenCallback = null,
            int responseBufferBytes = CactusNative.DefaultJsonBufferSize)
        {
            if (pcm16Mono == null || pcm16Mono.Length == 0)
            {
                throw new ArgumentException("PCM audio buffer is required.", "pcm16Mono");
            }

            return TranscribeInternal(null, pcm16Mono, prompt, optionsJson, tokenCallback, responseBufferBytes);
        }

        public CactusTranscriptionStream StartStreamingTranscription(string optionsJson = null)
        {
            EnsureNotDisposed();

            var streamHandle = CactusRuntime.InvokeNative(() => CactusNative.StreamTranscribeStart(_handle, optionsJson));
            if (streamHandle == IntPtr.Zero)
            {
                throw CreateOperationException("start streaming transcription");
            }

            return new CactusTranscriptionStream(streamHandle);
        }

        public void Dispose()
        {
            if (_handle == IntPtr.Zero)
            {
                return;
            }

            CactusRuntime.InvokeNative(() => CactusNative.Destroy(_handle));
            _handle = IntPtr.Zero;
            GC.SuppressFinalize(this);
        }

        private string TranscribeInternal(
            string audioFilePath,
            byte[] pcm16Mono,
            string prompt,
            string optionsJson,
            Action<string, uint> tokenCallback,
            int responseBufferBytes)
        {
            EnsureNotDisposed();
            ValidateBufferSize(responseBufferBytes);

            var responseBuffer = new byte[responseBufferBytes];
            CactusNative.TokenCallback callbackBridge = null;

            if (tokenCallback != null)
            {
                callbackBridge = (token, tokenId, userData) =>
                {
                    tokenCallback(CactusNative.PtrToString(token), tokenId);
                };
            }

            var result = CactusRuntime.InvokeNative(() =>
                CactusNative.Transcribe(
                    _handle,
                    audioFilePath,
                    prompt,
                    responseBuffer,
                    CactusNative.ToSizeT(responseBuffer.Length),
                    optionsJson,
                    callbackBridge,
                    IntPtr.Zero,
                    pcm16Mono,
                    CactusNative.ToSizeT(pcm16Mono == null ? 0 : pcm16Mono.Length)));

            GC.KeepAlive(callbackBridge);

            if (result < 0)
            {
                throw CreateOperationException("transcribe audio");
            }

            return CactusNative.DecodeNullTerminatedUtf8(responseBuffer);
        }

        private InvalidOperationException CreateOperationException(string operation)
        {
            var error = CactusNative.GetLastError();
            if (string.IsNullOrWhiteSpace(error))
            {
                error = "Unknown native error.";
            }

            return new InvalidOperationException("Failed to " + operation + ": " + error);
        }

        private void EnsureNotDisposed()
        {
            if (_handle == IntPtr.Zero)
            {
                throw new ObjectDisposedException("CactusModel");
            }
        }

        private static void ValidateBufferSize(int responseBufferBytes)
        {
            if (responseBufferBytes <= 0)
            {
                throw new ArgumentOutOfRangeException("responseBufferBytes", "Response buffer must be positive.");
            }
        }
    }

    public sealed class CactusTranscriptionStream : IDisposable
    {
        private IntPtr _streamHandle;

        internal CactusTranscriptionStream(IntPtr streamHandle)
        {
            _streamHandle = streamHandle;
        }

        public bool IsDisposed
        {
            get { return _streamHandle == IntPtr.Zero; }
        }

        public string ProcessJson(byte[] pcm16Mono, int responseBufferBytes = CactusNative.DefaultJsonBufferSize)
        {
            EnsureNotDisposed();

            if (pcm16Mono == null || pcm16Mono.Length == 0)
            {
                throw new ArgumentException("PCM audio buffer is required.", "pcm16Mono");
            }

            if (responseBufferBytes <= 0)
            {
                throw new ArgumentOutOfRangeException("responseBufferBytes", "Response buffer must be positive.");
            }

            var responseBuffer = new byte[responseBufferBytes];
            var result = CactusRuntime.InvokeNative(() =>
                CactusNative.StreamTranscribeProcess(
                    _streamHandle,
                    pcm16Mono,
                    CactusNative.ToSizeT(pcm16Mono.Length),
                    responseBuffer,
                    CactusNative.ToSizeT(responseBuffer.Length)));

            if (result < 0)
            {
                throw new InvalidOperationException("Failed to process streaming transcription chunk: " + CactusNative.GetLastError());
            }

            return CactusNative.DecodeNullTerminatedUtf8(responseBuffer);
        }

        public string StopJson(int responseBufferBytes = CactusNative.DefaultJsonBufferSize)
        {
            EnsureNotDisposed();

            if (responseBufferBytes <= 0)
            {
                throw new ArgumentOutOfRangeException("responseBufferBytes", "Response buffer must be positive.");
            }

            var responseBuffer = new byte[responseBufferBytes];
            var result = CactusRuntime.InvokeNative(() =>
                CactusNative.StreamTranscribeStop(
                    _streamHandle,
                    responseBuffer,
                    CactusNative.ToSizeT(responseBuffer.Length)));

            if (result < 0)
            {
                throw new InvalidOperationException("Failed to stop streaming transcription: " + CactusNative.GetLastError());
            }

            _streamHandle = IntPtr.Zero;
            return CactusNative.DecodeNullTerminatedUtf8(responseBuffer);
        }

        public void Dispose()
        {
            if (_streamHandle == IntPtr.Zero)
            {
                return;
            }

            try
            {
                CactusRuntime.InvokeNative(() => CactusNative.StreamTranscribeStop(_streamHandle, null, UIntPtr.Zero));
            }
            catch
            {
            }
            finally
            {
                _streamHandle = IntPtr.Zero;
                GC.SuppressFinalize(this);
            }
        }

        private void EnsureNotDisposed()
        {
            if (_streamHandle == IntPtr.Zero)
            {
                throw new ObjectDisposedException("CactusTranscriptionStream");
            }
        }
    }
}
