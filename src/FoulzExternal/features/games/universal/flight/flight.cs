using System;
using System.Threading;
using System.Runtime.InteropServices;
using FoulzExternal.SDK;
using FoulzExternal.SDK.structures;
using FoulzExternal.SDK.caches;
using FoulzExternal.storage;
using Offsets;
using Options;

namespace FoulzExternal.features.games.universal.flight
{
    internal static class Flight
    {
        private static bool _isRunning;
        private static Thread? _workerThread;
        private static readonly object _lock = new();
        private static CancellationTokenSource? _cts;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        public static void Start()
        {
            lock (_lock)
            {
                if (_isRunning) return;
                _isRunning = true;
                _cts = new CancellationTokenSource();
                _workerThread = new Thread(() => Worker(_cts.Token)) { IsBackground = true };
                _workerThread.Start();
            }
        }

        public static void Stop()
        {
            lock (_lock)
            {
                _isRunning = false;
                _cts?.Cancel();
                _workerThread?.Join(500);
                _cts?.Dispose();
            }
        }

        private static void Worker(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _isRunning)
            {
                try
                {
                    if (!Options.Settings.Flight.VFlight || !Options.Settings.Flight.VFlightBind.IsPressed())
                    {
                        Thread.Sleep(50);
                        continue;
                    }

                    var lp = Storage.LocalPlayerInstance;
                    if (!lp.IsValid) { Thread.Sleep(8); continue; }

                    var chr = lp.GetCharacter();
                    if (!chr.IsValid) { Thread.Sleep(8); continue; }

                    var hrp = chr.FindFirstChild("HumanoidRootPart");
                    if (!hrp.IsValid || hrp.Address == 0) { Thread.Sleep(8); continue; }

                    var cam = Storage.WorkspaceInstance.FindFirstChild("Camera");
                    if (!cam.IsValid) { Thread.Sleep(8); continue; }

                    var rot = SDK.Instance.Mem.Read<Matrix3x3>(cam.Address + Offsets.Camera.Rotation);
                    var forward = new Vector3 { x = rot.r02, y = rot.r12, z = rot.r22 };
                    var right   = new Vector3 { x = rot.r00, y = rot.r10, z = rot.r20 };

                    Vector3 dir = default;
                    bool moving = false;

                    if (IsKeyDown(0x57)) { dir -= forward; moving = true; } // W
                    if (IsKeyDown(0x53)) { dir += forward; moving = true; } // S
                    if (IsKeyDown(0x41)) { dir -= right;   moving = true; } // A
                    if (IsKeyDown(0x44)) { dir += right;   moving = true; } // D
                    if (IsKeyDown(0x20)) { dir.y += 1f;    moving = true; } // Space
                    if (IsKeyDown(0x11)) { dir.y -= 1f;    moving = true; } // Ctrl

                    if (moving)
                    {
                        if (dir.Magnitude() > 0.01f) dir = dir.Normalize();
                        SDK.Instance.Mem.Write<Vector3>(hrp.Address + Offsets.BasePart.AssemblyLinearVelocity, dir * Options.Settings.Flight.VFlightSpeed);
                    }
                    else
                    {
                        SDK.Instance.Mem.Write<Vector3>(hrp.Address + Offsets.BasePart.AssemblyLinearVelocity, default);
                    }
                }
                catch { }
                Thread.Sleep(10);
            }
        }

        private static bool IsKeyDown(int vKey) => (GetAsyncKeyState(vKey) & 0x8000) != 0;
    