using System;
using System.Threading;
using FoulzExternal.SDK;
using Offsets;
using Options;
using FoulzExternal.SDK.structures;
using FoulzExternal.SDK.caches;
using FoulzExternal.games.universal.visuals;
using FoulzExternal.storage;

namespace FoulzExternal.features.games.universal.desync
{
    internal static class Desync
    {
        private static bool _isRunning;
        private static Thread? _workerThread;
        private static readonly object _lock = new();
        private static CancellationTokenSource? _cts;

        private static bool _isActive;
        private static bool _keyHeld;
        private static Vector3 _spawnPosition = default;

        private static int _lastBindHash;
        private static DateTime _debounceUntil = DateTime.MinValue;

        public class Scene { public bool Active; public Vector3 Position; }

        public static void Start()
        {
            if (_isRunning) return;
            _isRunning = true;
            _cts = new CancellationTokenSource();
            _workerThread = new Thread(() => Worker(_cts.Token)) { IsBackground = true };
            _workerThread.Start();
        }

        public static void Stop()
        {
            _isRunning = false;
            _cts?.Cancel();
            _workerThread?.Join(500);
            Cleanup();
        }

        public static Scene GetSceneSnapshot()
        {
            lock (_lock) return new Scene { Active = _isActive, Position = _spawnPosition };
        }

        private static Vector3 GetLocalPosition()
        {
            try
            {
                var lp = Storage.LocalPlayerInstance;
                if (!lp.IsValid) return default;
                var players = playerobjects.CachedPlayerObjects;
                if (players == null) return default;
                var localObj = players.FirstOrDefault(x => x.address == lp.Address);
                if (localObj.address == 0 || !localObj.HumanoidRootPart.IsValid) return default;
                return visuals.GetPos(localObj.HumanoidRootPart, new Dictionary<long, long>());
            }
            catch { return default; }
        }

        private static int GetBindHash()
        {
            var b = Options.Settings.Network.DeSyncBind;
            if (b == null) return 0;
            return (b.Key & 0xFFFF) | ((b.MouseButton & 0xFF) << 16) ^ ((b.ControllerButton & 0xFF) << 24);
        }

        private static void Worker(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _isRunning)
            {
                try
                {
                    if (!Options.Settings.Network.DeSync)
                    {
                        if (_isActive) Deactivate();
                        Thread.Sleep(200);
                        continue;
                    }

                    int cur = GetBindHash();
                    if (cur != _lastBindHash)
                    {
                        _lastBindHash = cur;
                        _debounceUntil = DateTime.UtcNow.AddMilliseconds(300);
                    }

                    bool pressed = Options.Settings.Network.DeSyncBind.IsPressed();
                    if (DateTime.UtcNow < _debounceUntil) pressed = false;

                    if (pressed && !_keyHeld) Activate();
                    else if (!pressed && _keyHeld) Deactivate();

                    Thread.Sleep(30);
                }
                catch { Thread.Sleep(100); }
            }
            Cleanup();
        }

        private static void Activate()
        {
            _keyHeld = true;
            _isActive = true;
            lock (_lock) _spawnPosition = GetLocalPosition();
            try { SDK.Instance.Mem.Write<bool>(SDK.Instance.Mem.Base + FFlags.NextGenReplicatorEnabledWrite4, true); } catch { }
        }

        private static void Deactivate()
        {
            _keyHeld = false;
            _isActive = false;
            lock (_lock) _spawnPosition = default;
            try { SDK.Instance.Mem.Write<bool>(SDK.Instance.Mem.Base + FFlags.NextGenReplicatorEnabledWrite4, false); } catch { }
        }

        private static void Cleanup()
        {
            try { SDK.Instance.Mem.Write<bool>(SDK.Instance.Mem.Base + FFlags.NextGenReplicatorEnabledWrite4, false); } catch { }
            _isActive = false;
            lock (_lock) _spawnPosition = default;
            _keyHeld = false;
        }
    }
}