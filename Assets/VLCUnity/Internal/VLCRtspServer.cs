using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using UnityEngine;
using UnityEngine.Events;

namespace LibVLCSharp
{
    /// <summary>
    /// Publishes a file or network media source through the RTSP server built into LibVLC.
    /// The Android LibVLC binaries bundled with VLCUnity include the required RTP/RTSP
    /// stream-output modules.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VLCRtspServer : MonoBehaviour
    {
        public enum ServerState
        {
            Stopped,
            Starting,
            Running,
            Error
        }

        [Serializable]
        public sealed class ServerStateEvent : UnityEvent<ServerState> { }

        [Serializable]
        public sealed class ServerMessageEvent : UnityEvent<string> { }

        [Header("Source")]
        [Tooltip("A file URI/path or a network URL that LibVLC can open. This component relays that source; it does not capture a Unity RenderTexture or Android camera directly.")]
        public string sourceMediaPath = "https://download.blender.org/peach/bigbuckbunny_movies/big_buck_bunny_1080p_stereo.avi";

        [Tooltip("Extra media options passed to LibVLC, for example :network-caching=300.")]
        public string[] sourceMediaOptions = Array.Empty<string>();

        [Tooltip("Repeat seekable sources indefinitely.")]
        public bool loopSource = true;

        [Header("RTSP endpoint")]
        [Range(1, 65535)]
        public int port = 8554;

        [Tooltip("Path portion of the RTSP URL, without a leading slash.")]
        public string streamPath = "stream";

        [Tooltip("Start publishing when this component starts.")]
        public bool startOnAwake;

        [Header("Transcoding")]
        [Tooltip("Transcode to H.264/AAC for broad client compatibility. Leave disabled when the source is already client-compatible to reduce Android CPU and battery use.")]
        public bool transcodeToH264Aac;

        [Min(64)] public int videoBitrateKbps = 1500;
        [Min(16)] public int audioBitrateKbps = 128;
        [Min(8000)] public int audioSampleRate = 44100;

        [Header("Runtime")]
        [Tooltip("Keep the Android screen awake while publishing. This does not turn the component into an Android background/foreground service.")]
        public bool keepScreenAwake = true;

        [Tooltip("Forward LibVLC diagnostic messages to the Unity console.")]
        public bool logToConsole = true;

        public ServerState State { get; private set; } = ServerState.Stopped;
        public bool IsRunning => State == ServerState.Running && _mediaPlayer != null && _mediaPlayer.IsPlaying;
        public string ListenUrl => $"rtsp://0.0.0.0:{port}/{NormalizedStreamPath}";

        public ServerStateEvent OnStateChanged = new();
        public ServerMessageEvent OnError = new();

        private readonly ConcurrentQueue<Action> _mainThreadActions = new();
        private LibVLC _libVLC;
        private MediaPlayer _mediaPlayer;
        private Media _media;
        private int _previousSleepTimeout;
        private bool _sleepTimeoutChanged;
        private bool _isStopping;

        private string NormalizedStreamPath
        {
            get
            {
                var value = streamPath?.Trim().Trim('/') ?? string.Empty;
                return string.IsNullOrEmpty(value) ? "stream" : value;
            }
        }

        private void Start()
        {
            if (startOnAwake)
                StartServer();
        }

        private void Update()
        {
            while (_mainThreadActions.TryDequeue(out var action))
                action?.Invoke();
        }

        private void OnDestroy()
        {
            StopServer();
        }

        /// <summary>
        /// Starts the RTSP endpoint. Clients should connect with a URL returned by
        /// GetClientUrls(), not the wildcard ListenUrl.
        /// </summary>
        public bool StartServer()
        {
            if (State == ServerState.Starting || State == ServerState.Running)
                return true;

            try
            {
                ValidateConfiguration();
                StopNativeObjects();
                SetState(ServerState.Starting);

                Core.Initialize(Application.dataPath);
                _libVLC = new LibVLC(logToConsole,
                    "--no-video-title-show",
                    "--aout=adummy",
                    "--vout=dummy");

                if (logToConsole)
                    _libVLC.Log += OnLibVLCLog;

                _mediaPlayer = new MediaPlayer(_libVLC);
                _mediaPlayer.Playing += OnPlaying;
                _mediaPlayer.EncounteredError += OnEncounteredError;
                _mediaPlayer.Stopped += OnStopped;

                var options = new List<string>(sourceMediaOptions?.Where(value => !string.IsNullOrWhiteSpace(value))
                                                ?? Enumerable.Empty<string>())
                {
                    BuildStreamOutputOption(),
                    ":sout-keep"
                };

                if (loopSource)
                    options.Add(":input-repeat=-1");

                _media = new Media(ToMediaUri(sourceMediaPath), options.ToArray());
                _mediaPlayer.Media = _media;

                if (keepScreenAwake)
                {
                    _previousSleepTimeout = Screen.sleepTimeout;
                    Screen.sleepTimeout = SleepTimeout.NeverSleep;
                    _sleepTimeoutChanged = true;
                }

                Log($"Starting RTSP server at {ListenUrl}");
                if (!_mediaPlayer.Play())
                    throw new InvalidOperationException("LibVLC rejected the media and did not start playback.");

                return true;
            }
            catch (Exception exception)
            {
                Fail($"Unable to start the RTSP server: {exception.Message}");
                StopNativeObjects();
                return false;
            }
        }

        public void StopServer()
        {
            if (_isStopping)
                return;

            _isStopping = true;
            try
            {
                StopNativeObjects();
                RestoreSleepTimeout();
                SetState(ServerState.Stopped);
            }
            finally
            {
                _isStopping = false;
            }
        }

        /// <summary>
        /// Returns RTSP URLs for active non-loopback IPv4 interfaces on this device.
        /// An empty result means that no usable interface is currently connected.
        /// </summary>
        public string[] GetClientUrls()
        {
            var addresses = new HashSet<string>();

            try
            {
                foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                        networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;

                    foreach (var address in networkInterface.GetIPProperties().UnicastAddresses)
                    {
                        if (address.Address.AddressFamily == AddressFamily.InterNetwork &&
                            !IPAddress.IsLoopback(address.Address))
                            addresses.Add($"rtsp://{address.Address}:{port}/{NormalizedStreamPath}");
                    }
                }
            }
            catch (Exception exception)
            {
                Log($"Could not enumerate local IP addresses: {exception.Message}");
            }

            return addresses.OrderBy(value => value).ToArray();
        }

        private string BuildStreamOutputOption()
        {
            var endpoint = $"rtsp://:{port}/{NormalizedStreamPath}";
            if (!transcodeToH264Aac)
                return $":sout=#rtp{{sdp={endpoint}}}";

            var transcode =
                $"transcode{{vcodec=h264,vb={videoBitrateKbps},acodec=mp4a,ab={audioBitrateKbps},channels=2,samplerate={audioSampleRate}}}";
            return $":sout=#{transcode}:rtp{{sdp={endpoint}}}";
        }

        private void ValidateConfiguration()
        {
            if (string.IsNullOrWhiteSpace(sourceMediaPath))
                throw new InvalidOperationException("Source Media Path is empty.");
            if (port < 1 || port > 65535)
                throw new ArgumentOutOfRangeException(nameof(port), "The RTSP port must be between 1 and 65535.");
            if (NormalizedStreamPath.Any(char.IsWhiteSpace))
                throw new InvalidOperationException("Stream Path cannot contain whitespace.");
        }

        private static Uri ToMediaUri(string source)
        {
            var trimmed = source.Trim().Trim('"');
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
                return uri;

            return new Uri(Path.GetFullPath(trimmed));
        }

        private void StopNativeObjects()
        {
            if (_mediaPlayer != null)
            {
                _mediaPlayer.Playing -= OnPlaying;
                _mediaPlayer.EncounteredError -= OnEncounteredError;
                _mediaPlayer.Stopped -= OnStopped;
                _mediaPlayer.Stop();
                _mediaPlayer.Dispose();
                _mediaPlayer = null;
            }

            _media?.Dispose();
            _media = null;

            if (_libVLC != null)
            {
                if (logToConsole)
                    _libVLC.Log -= OnLibVLCLog;
                _libVLC.Dispose();
                _libVLC = null;
            }
        }

        private void RestoreSleepTimeout()
        {
            if (!_sleepTimeoutChanged)
                return;

            Screen.sleepTimeout = _previousSleepTimeout;
            _sleepTimeoutChanged = false;
        }

        private void OnPlaying(object sender, EventArgs eventArgs)
        {
            _mainThreadActions.Enqueue(() =>
            {
                SetState(ServerState.Running);
                var urls = GetClientUrls();
                Log(urls.Length > 0
                    ? $"RTSP server is running: {string.Join(", ", urls)}"
                    : $"RTSP server is running on port {port}; no LAN IPv4 address was found yet.");
            });
        }

        private void OnEncounteredError(object sender, EventArgs eventArgs)
        {
            _mainThreadActions.Enqueue(() => Fail(
                "LibVLC encountered an error. Check the preceding LibVLC log; common causes are an unavailable source or a port already in use."));
        }

        private void OnStopped(object sender, EventArgs eventArgs)
        {
            if (!_isStopping)
                _mainThreadActions.Enqueue(() => SetState(ServerState.Stopped));
        }

        private void OnLibVLCLog(object sender, LogEventArgs eventArgs)
        {
            try
            {
                Log(eventArgs.FormattedLog);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[VLCRtspServer] Failed to format a LibVLC log message: {exception.Message}");
            }
        }

        private void Fail(string message)
        {
            Debug.LogError($"[VLCRtspServer] {message}", this);
            SetState(ServerState.Error);
            OnError?.Invoke(message);
            RestoreSleepTimeout();
        }

        private void SetState(ServerState state)
        {
            if (State == state)
                return;

            State = state;
            OnStateChanged?.Invoke(state);
        }

        private void Log(string message)
        {
            if (logToConsole)
                Debug.Log($"[VLCRtspServer] {message}", this);
        }
    }
}
