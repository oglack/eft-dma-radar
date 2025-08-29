global using static eft_dma_radar.Tarkov.MemoryInterface;
using eft_dma_radar.Tarkov.API;
using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.Tarkov.GameWorld;
using eft_dma_radar.Tarkov.GameWorld.Exits;
using eft_dma_radar.Tarkov.GameWorld.Explosives;
using eft_dma_radar.Tarkov.Loot;
using eft_dma_radar.UI.Misc;
using eft_dma_shared.Common.DMA;
using eft_dma_shared.Common.DMA.ScatterAPI;
using eft_dma_shared.Common.Misc;
using eft_dma_shared.Common.Unity;
using System.Drawing;
using System.Runtime;
using Vmmsharp;

namespace eft_dma_radar.Tarkov
{
    internal static class MemoryInterface
    {
        private static MemDMA _actualMemory;
        private static SafeMemoryProxy _safeMemory;

        /// <summary>
        /// Safe Memory Interface that works in both Normal and Safe mode
        /// </summary>
        public static SafeMemoryProxy Memory
        {
            get
            {
                // Safety net: if Memory is accessed before ModuleInit, create a safe proxy
                if (_safeMemory == null)
                {
                    LoneLogging.WriteLine("[Warning] Memory accessed before ModuleInit - creating emergency safe proxy");
                    _safeMemory = new SafeMemoryProxy(null);
                }
                return _safeMemory;
            }
        }

        /// <summary>
        /// Check if DMA is available
        /// </summary>
        public static bool IsDMAAvailable => _actualMemory != null && Program.CurrentMode == ApplicationMode.Normal;

        /// <summary>
        /// Initialize the Memory Interface.
        /// </summary>
        public static void ModuleInit()
        {
            if (Program.CurrentMode == ApplicationMode.Normal)
            {
                _actualMemory = new MemDMA();
                _safeMemory = new SafeMemoryProxy(_actualMemory);
                LoneLogging.WriteLine("DMA Memory Interface initialized - Normal Mode");
            }
            else
            {
                _actualMemory = null;
                _safeMemory = new SafeMemoryProxy(null);
                LoneLogging.WriteLine("Safe Memory Interface initialized - Safe Mode (DMA disabled)");
            }
        }
    }

    /// <summary>
    /// DMA Memory Module.
    /// </summary>
    public sealed class MemDMA : MemDMABase
    {
        #region Fields/Properties/Constructor/Thread Worker

        private const string _processName = "EscapeFromTarkov.exe";

        /// <summary>
        /// App Configuration.
        /// </summary>
        private static Config Config => Program.Config;

        /// <summary>
        /// Current Map ID.
        /// </summary>
        public string MapID => Game?.MapID;
        public override bool IsOffline => LocalGameWorld.IsOffline;
        
        /// <summary>
        /// True if currently in a raid/match, otherwise False.
        /// </summary>
        public override bool InRaid => Game?.InRaid ?? false;
        /// <summary>
        /// True if the raid countdown has completed and the raid has started.
        /// </summary>
        public override bool RaidHasStarted => Game?.RaidHasStarted ?? false;

        private bool _ready;
        /// <summary>
        /// True if Startup was successful and waiting for raid.
        /// </summary>
        public override bool Ready => _ready;
        private bool _starting;
        /// <summary>
        /// True if in the process of starting the game.
        /// </summary>
        public override bool Starting => _starting;

        public IReadOnlyCollection<Player> Players => Game?.Players;
        public IReadOnlyCollection<IExplosiveItem> Explosives => Game?.Explosives;
        public IReadOnlyCollection<IExitPoint> Exits => Game?.Exits;
        
        public LocalPlayer LocalPlayer => Game?.LocalPlayer;
        public LootManager Loot => Game?.Loot;
        public QuestManager QuestManager => Game?.QuestManager;
        public LocalGameWorld Game { get; private set; }

        public MemDMA() : base(Config.FpgaAlgo, Config.MemMapEnabled)
        {
            GameStarted += MemDMA_GameStarted;
            GameStopped += MemDMA_GameStopped;
            RaidStarted += MemDMA_RaidStarted;
            RaidStopped += MemDMA_RaidStopped;

            new Thread(MemoryPrimaryWorker)
            {
                IsBackground = true
            }.Start(); // Start Memory Thread after successful startup
        }

        /// <summary>
        /// Main worker thread to perform DMA Reads on.
        /// </summary>
        private void MemoryPrimaryWorker()
        {
            LoneLogging.WriteLine("Memory thread starting...");

            while (!MainWindow.Initialized)
            {
                LoneLogging.WriteLine("[Waiting] Main window not ready...");
                Thread.Sleep(100);
            }

            while (true)
            {
                try
                {
                    while (true)
                    {
                        RunStartupLoop();
                        OnGameStarted();
                        RunGameLoop();
                        OnGameStopped();
                    }
                }
                catch (Exception ex)
                {
                    LoneLogging.WriteLine($"FATAL ERROR on Memory Thread: {ex}");
                    if (MainWindow.Window != null)
                        NotificationsShared.Warning($"FATAL ERROR on Memory Thread");
                    OnGameStopped();
                    Thread.Sleep(1000);
                }
            }
        }


        #endregion

        #region Startup / Main Loop

        /// <summary>
        /// Starts up the Game Process and all mandatory modules.
        /// Returns to caller when the Game is ready.
        /// </summary>
        private void RunStartupLoop()
        {
            LoneLogging.WriteLine("New Game Startup");
            while (true) // Startup loop
            {
                try
                {
                    FullRefresh();
                    ResourceJanitor.Run();
                    LoadProcess();
                    LoadModules();
                    _starting = true;
                    MonoLib.InitializeEFT();
                    InputManager.Initialize();
                    CameraManager.Initialize();
                    _ready = true;
                    LoneLogging.WriteLine("Game Startup [OK]");
                    if (MainWindow.Window != null)
                        NotificationsShared.Info("Game Startup [OK]");
                    break;
                }
                catch (Exception ex)
                {
                    LoneLogging.WriteLine($"Game Startup [FAIL]: {ex}");
                    OnGameStopped();
                    Thread.Sleep(1000);
                }
            }
        }

        /// <summary>
        /// Main Game Loop Method.
        /// Returns to caller when Game is no longer running.
        /// </summary>
        private void RunGameLoop()
        {
            while (true)
            {
                try
                {
                    using (var game = Game = LocalGameWorld.CreateGameInstance())
                    {
                        OnRaidStarted();
                        game.Start();
                        while (game.InRaid)
                        {
                            if (_restartRadar)
                            {
                                LoneLogging.WriteLine("Restarting Radar per User Request.");
                                if (MainWindow.Window != null)
                                    NotificationsShared.Info("Restarting Radar per User Request.");
                                _restartRadar = false;
                                break;
                            }
                            game.Refresh();
                            Thread.Sleep(133);
                        }
                    }
                }
                catch (GameNotRunning) { break; }
                catch (Exception ex)
                {
                    LoneLogging.WriteLine($"CRITICAL ERROR in Game Loop: {ex}");
                    if (MainWindow.Window != null)
                        NotificationsShared.Warning($"CRITICAL ERROR in Game Loop");
                    break;
                }
                finally
                {
                    OnRaidStopped();
                    Thread.Sleep(100);
                }
            }
            LoneLogging.WriteLine("Game is no longer running!");
            if (MainWindow.Window != null)
                NotificationsShared.Warning("Game is no longer running!");
        }

        /// <summary>
        /// Raised when the game is started.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MemDMA_GameStarted(object sender, EventArgs e)
        {
            _syncProcessRunning.Set();
        }

        /// <summary>
        /// Raised when the game is stopped.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MemDMA_GameStopped(object sender, EventArgs e)
        {
            _restartRadar = default;
            _starting = default;
            _ready = default;
            UnityBase = default;
            MonoBase = default;
            _syncProcessRunning.Reset();
            MonoLib.Reset();
        }


        private void MemDMA_RaidStopped(object sender, EventArgs e)
        {
            GCSettings.LatencyMode = GCLatencyMode.Interactive;
            _syncInRaid.Reset();
            Game = null;
        }

        private void MemDMA_RaidStarted(object sender, EventArgs e)
        {
            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
            _syncInRaid.Set();
        }

        /// <summary>
        /// Obtain the PID for the Game Process.
        /// </summary>
        private void LoadProcess()
        {
            var tmpProcess = _hVMM.Process(_processName);
            if (tmpProcess == null)
                throw new Exception($"Unable to find '{_processName}'");

            Process = tmpProcess;
        }

        /// <summary>
        /// Gets the Game Process Base Module Addresses.
        /// </summary>
        private void LoadModules()
        {
            var unityBase = Process.GetModuleBase("UnityPlayer.dll");
            ArgumentOutOfRangeException.ThrowIfZero(unityBase, nameof(unityBase));
            var monoBase = Process.GetModuleBase("mono-2.0-bdwgc.dll");
            ArgumentOutOfRangeException.ThrowIfZero(monoBase, nameof(monoBase));

            UnityBase = unityBase;
            MonoBase = monoBase;
        }

        #endregion

        #region R/W Methods
        /// <summary>
        /// Write value type/struct to specified address.
        /// </summary>
        /// <typeparam name="T">Specified Value Type.</typeparam>
        /// <param name="game">Game instance to write to.</param>
        /// <param name="addr">Address to write to.</param>
        /// <param name="value">Value to write.</param>
        public void WriteValue<T>(LocalGameWorld game, ulong addr, T value)
            where T : unmanaged
        {
            if (!game.IsSafeToWriteMem)
                throw new Exception("Not safe to write!");
            WriteValue(addr, value);
        }

        /// <summary>
        /// Write byte array buffer to Memory Address.
        /// </summary>
        /// <param name="game">Current Game Instance.</param>
        /// <param name="addr">Address to write to.</param>
        /// <param name="buffer">Buffer to write.</param>
        public void WriteBuffer<T>(LocalGameWorld game, ulong addr, Span<T> buffer)
            where T : unmanaged
        {
            if (!game.IsSafeToWriteMem)
                throw new Exception("Not safe to write!");
            WriteBuffer(addr, buffer);
        }
        public bool TryReadValue<T>(ulong addr, out T value) where T : unmanaged
        {
            try
            {
                value = Memory.ReadValue<T>(addr);
                return true;
            }
            catch
            {
                value = default;
                return false;
            }
        }
        public bool IsValid(ulong address)
        {
            try
            {
                if (address == 0) return false;

                // Correct method from your MemDMA implementation
                _ = Memory.ReadValue<byte>(address); 
                return true;
            }
            catch
            {
                return false;
            }
        }


        #endregion

        #region Misc

        /// <summary>
        /// Throws a special exception if no longer in game.
        /// </summary>
        /// <exception cref="GameNotRunning"></exception>
        public void ThrowIfNotInGame()
        {
            FullRefresh();
            for (var i = 0; i < 5; i++)
                try
                {
                    var tempProcess = _hVMM.Process(_processName);
                    if (tempProcess is null)
                        throw new Exception();
                    return;
                }
                catch
                {
                    Thread.Sleep(150);
                }

            throw new GameNotRunning("Not in game!");
        }

        /// <summary>
        /// Get the Monitor Resolution from the Game Monitor.
        /// </summary>
        /// <returns>Monitor Resolution Result</returns>
        public Rectangle GetMonitorRes()
        {
            try
            {
                var gfx = ReadPtr(UnityBase + UnityOffsets.ModuleBase.GfxDevice, false);
                var res = ReadValue<Rectangle>(gfx + UnityOffsets.GfxDeviceClient.Viewport, false);
                if (res.Width <= 0 || res.Width > 10000 ||
                    res.Height <= 0 || res.Height > 5000)
                    throw new ArgumentOutOfRangeException(nameof(res));
                return res;
            }
            catch (Exception ex)
            {
                throw new Exception("ERROR Getting Game Monitor Res", ex);
            }
        }

        public sealed class GameNotRunning : Exception
        {
            public GameNotRunning()
            {
            }

            public GameNotRunning(string message)
                : base(message)
            {
            }

            public GameNotRunning(string message, Exception inner)
                : base(message, inner)
            {
            }
        }

        #endregion

        #region NativeHook Interop
        /// <summary>
        /// Get the Code Cave Address for NativeHook.
        /// </summary>
        /// <exception cref="Exception"></exception>
        public override ulong GetCodeCave()
        {
            var @class = MonoLib.MonoClass.Find("Assembly-CSharp", "EFT.TarkovApplication", out _);
            return @class.FindJittedMethod("ConfigureApplication");
            // Already validated in called funcs
        }
        #endregion
    }

    public class SafeMemoryProxy
    {
        private readonly MemDMA _actualMemory;

        public SafeMemoryProxy(MemDMA actualMemory)
        {
            _actualMemory = actualMemory;
        }

        public QuestManager QuestManager => _actualMemory?.QuestManager;
        public LootManager Loot => _actualMemory?.Loot;
        public LocalPlayer LocalPlayer => _actualMemory?.LocalPlayer;
        public IReadOnlyCollection<Player> Players => _actualMemory?.Players ?? new List<Player>();
        public IReadOnlyCollection<IExplosiveItem> Explosives => _actualMemory?.Explosives ?? new List<IExplosiveItem>();
        public IReadOnlyCollection<IExitPoint> Exits => _actualMemory?.Exits ?? new List<IExitPoint>();
        public LocalGameWorld Game => _actualMemory?.Game;
        public string MapID => _actualMemory?.MapID;
        public bool IsOffline => _actualMemory?.IsOffline ?? false;
        public bool InRaid => _actualMemory?.InRaid ?? false;
        public bool RaidHasStarted => _actualMemory?.RaidHasStarted ?? false;
        public bool Ready => _actualMemory?.Ready ?? false;
        public bool Starting => _actualMemory?.Starting ?? false;

        public ulong MonoBase => _actualMemory?.MonoBase ?? 0;
        public ulong UnityBase => _actualMemory?.UnityBase ?? 0;
        public VmmProcess Process => _actualMemory?.Process;
        public Vmm VmmHandle => _actualMemory?.VmmHandle;

        public bool RestartRadar
        {
            set
            {
                if (_actualMemory != null)
                    _actualMemory.RestartRadar = value;
            }
        }

        public bool TryReadValue<T>(ulong addr, out T value) where T : unmanaged
        {
            if (_actualMemory != null)
                return _actualMemory.TryReadValue(addr, out value);

            value = default;
            return false;
        }

        public bool IsValid(ulong address)
        {
            return _actualMemory?.IsValid(address) ?? false;
        }

        public T ReadValue<T>(ulong addr, bool useCache = true) where T : unmanaged, allows ref struct
        {
            if (_actualMemory == null)
            {
                LoneLogging.WriteLine($"[SafeMode] ReadValue<{typeof(T).Name}> skipped at 0x{addr:X}");
                return default(T);
            }
            return _actualMemory.ReadValue<T>(addr, useCache);
        }

        public void ReadValue<T>(ulong addr, out T result, bool useCache = true) where T : unmanaged, allows ref struct
        {
            if (_actualMemory == null)
            {
                result = default(T);
                return;
            }
            _actualMemory.ReadValue(addr, out result, useCache);
        }

        public T ReadValueEnsure<T>(ulong addr) where T : unmanaged, allows ref struct
        {
            if (_actualMemory == null)
            {
                LoneLogging.WriteLine($"[SafeMode] ReadValueEnsure<{typeof(T).Name}> skipped at 0x{addr:X}");
                return default(T);
            }
            return _actualMemory.ReadValueEnsure<T>(addr);
        }

        public void ReadValueEnsure<T>(ulong addr, out T result) where T : unmanaged, allows ref struct
        {
            if (_actualMemory == null)
            {
                result = default(T);
                return;
            }
            _actualMemory.ReadValueEnsure(addr, out result);
        }

        public ulong ReadPtr(ulong addr, bool useCache = true)
        {
            if (_actualMemory == null)
            {
                LoneLogging.WriteLine($"[SafeMode] ReadPtr skipped at 0x{addr:X}");
                return 0;
            }
            return _actualMemory.ReadPtr(addr, useCache);
        }

        public ulong ReadPtrChain(ulong addr, uint[] offsets, bool useCache = true)
        {
            if (_actualMemory == null)
            {
                LoneLogging.WriteLine($"[SafeMode] ReadPtrChain skipped at 0x{addr:X}");
                return 0;
            }
            return _actualMemory.ReadPtrChain(addr, offsets, useCache);
        }

        public void ReadBuffer<T>(ulong addr, Span<T> buffer, bool useCache = true, bool allowPartialRead = false) where T : unmanaged
        {
            if (_actualMemory == null)
            {
                LoneLogging.WriteLine($"[SafeMode] ReadBuffer<{typeof(T).Name}> skipped at 0x{addr:X}");
                buffer.Clear();
                return;
            }
            _actualMemory.ReadBuffer(addr, buffer, useCache, allowPartialRead);
        }
        /// <summary>
        /// Read memory into a buffer.
        /// </summary>
        public byte[] ReadBuffer(ulong addr, int size, bool useCache = true, bool allowIncompleteRead = false)
        {
            try
            {
                uint flags = useCache ? 0 : Vmm.FLAG_NOCACHE;
                var buf = Process.MemRead(addr, (uint)size, flags);
                if (!allowIncompleteRead && buf.Length != size)
                    throw new Exception("Incomplete memory read!");
                return buf;
            }
            catch (Exception ex)
            {
                throw new Exception($"[DMA] ERROR reading buffer at 0x{addr:X}", ex);
            }
        }   
        public void ReadBufferEnsure<T>(ulong addr, Span<T> buffer1) where T : unmanaged
        {
            if (_actualMemory == null)
            {
                buffer1.Clear();
                return;
            }
            _actualMemory.ReadBufferEnsure(addr, buffer1);
        }


        /// <summary> Evo
        /// Read memory into a buffer and validate the right bytes were received.
        /// </summary>
        public byte[] ReadBufferEnsureE(ulong addr, int size)
        {
            const int ValidationCount = 3;

            try
            {
                byte[][] buffers = new byte[ValidationCount][];
                for (int i = 0; i < ValidationCount; i++)
                {
                    buffers[i] = Process.MemRead(addr, (uint)size, Vmm.FLAG_NOCACHE);
                    
                    if (buffers[i].Length != size)
                        throw new Exception("Incomplete memory read!");
                }

                // Check that all arrays have the same contents
                for (int i = 1; i < ValidationCount; i++) // Start checking with second item in the array
                    if (!buffers[i].SequenceEqual(buffers[0])) // Compare against the first item in the array
                    {
                        LoneLogging.WriteLine($"[WARN] ReadBufferEnsure() -> 0x{addr:X} did not pass validation!");
                        return null;
                    }

                return buffers[0];
            }
            catch (Exception ex)
            {
                throw new Exception($"[DMA] ERROR reading buffer at 0x{addr:X}", ex);
            }
        }
        public string ReadString(ulong addr, int length, bool useCache = true)
        {
            if (_actualMemory == null)
            {
                LoneLogging.WriteLine($"[SafeMode] ReadString skipped at 0x{addr:X}");
                return string.Empty;
            }
            return _actualMemory.ReadString(addr, length, useCache);
        }

        public string ReadUnityString(ulong addr, int length = 64, bool useCache = true)
        {
            if (_actualMemory == null)
            {
                LoneLogging.WriteLine($"[SafeMode] ReadUnityString skipped at 0x{addr:X}");
                return string.Empty;
            }
            return _actualMemory.ReadUnityString(addr, length, useCache);
        }

        public void WriteValue<T>(LocalGameWorld game, ulong addr, T value) where T : unmanaged
        {
            if (_actualMemory == null)
            {
                LoneLogging.WriteLine($"[SafeMode] WriteValue<{typeof(T).Name}> skipped at 0x{addr:X} - Memory writes not available");
                return;
            }
            _actualMemory.WriteValue(game, addr, value);
        }

        public void WriteValue<T>(ulong addr, T value) where T : unmanaged, allows ref struct
        {
            if (_actualMemory == null)
            {
                LoneLogging.WriteLine($"[SafeMode] WriteValue<{typeof(T).Name}> skipped at 0x{addr:X} - Memory writes not available");
                return;
            }
            _actualMemory.WriteValue(addr, value);
        }

        public void WriteValue<T>(ulong addr, ref T value) where T : unmanaged, allows ref struct
        {
            if (_actualMemory == null)
            {
                LoneLogging.WriteLine($"[SafeMode] WriteValue<{typeof(T).Name}> (ref) skipped at 0x{addr:X} - Memory writes not available");
                return;
            }
            _actualMemory.WriteValue(addr, ref value);
        }

        public void WriteValueEnsure<T>(ulong addr, T value) where T : unmanaged, allows ref struct
        {
            if (_actualMemory == null)
            {
                LoneLogging.WriteLine($"[SafeMode] WriteValueEnsure<{typeof(T).Name}> skipped at 0x{addr:X} - Memory writes not available");
                return;
            }
            _actualMemory.WriteValueEnsure(addr, value);
        }

        public void WriteValueEnsure<T>(ulong addr, ref T value) where T : unmanaged, allows ref struct
        {
            if (_actualMemory == null)
            {
                LoneLogging.WriteLine($"[SafeMode] WriteValueEnsure<{typeof(T).Name}> (ref) skipped at 0x{addr:X} - Memory writes not available");
                return;
            }
            _actualMemory.WriteValueEnsure(addr, ref value);
        }

        public void WriteBuffer<T>(LocalGameWorld game, ulong addr, Span<T> buffer) where T : unmanaged
        {
            if (_actualMemory == null)
            {
                LoneLogging.WriteLine($"[SafeMode] WriteBuffer<{typeof(T).Name}> skipped at 0x{addr:X} - Memory writes not available");
                return;
            }
            _actualMemory.WriteBuffer(game, addr, buffer);
        }

        public void WriteBuffer<T>(ulong addr, Span<T> buffer) where T : unmanaged
        {
            if (_actualMemory == null)
            {
                LoneLogging.WriteLine($"[SafeMode] WriteBuffer<{typeof(T).Name}> skipped at 0x{addr:X} - Memory writes not available");
                return;
            }
            _actualMemory.WriteBuffer(addr, buffer);
        }

        public void WriteBufferEnsure<T>(ulong addr, Span<T> buffer) where T : unmanaged
        {
            if (_actualMemory == null)
            {
                LoneLogging.WriteLine($"[SafeMode] WriteBufferEnsure<{typeof(T).Name}> skipped at 0x{addr:X} - Memory writes not available");
                return;
            }
            _actualMemory.WriteBufferEnsure(addr, buffer);
        }

        public void ReadScatter(IScatterEntry[] entries, bool useCache = true)
        {
            if (_actualMemory == null)
            {
                LoneLogging.WriteLine($"[SafeMode] ReadScatter skipped ({entries?.Length ?? 0} entries)");
                if (entries != null)
                {
                    foreach (var entry in entries)
                        entry.IsFailed = true;
                }
                return;
            }
            _actualMemory.ReadScatter(entries, useCache);
        }

        public void ReadCache(params ulong[] va)
        {
            if (_actualMemory == null)
            {
                LoneLogging.WriteLine($"[SafeMode] ReadCache skipped");
                return;
            }
            _actualMemory.ReadCache(va);
        }

        public void FullRefresh()
        {
            if (_actualMemory == null)
            {
                LoneLogging.WriteLine($"[SafeMode] FullRefresh skipped");
                return;
            }
            _actualMemory.FullRefresh();
        }

        public ulong GetExport(string module, string name)
        {
            if (_actualMemory == null)
            {
                LoneLogging.WriteLine($"[SafeMode] GetExport skipped for {module}::{name}");
                return 0;
            }
            return _actualMemory.GetExport(module, name);
        }

        public VmmScatterMemory GetScatter(uint flags)
        {
            if (_actualMemory == null)
            {
                LoneLogging.WriteLine($"[SafeMode] GetScatter skipped");
                return null;
            }
            return _actualMemory.GetScatter(flags);
        }

        public ulong FindSignature(string signature, ulong rangeStart, ulong rangeEnd, VmmProcess process)
        {
            if (_actualMemory == null)
            {
                LoneLogging.WriteLine($"[SafeMode] FindSignature skipped");
                return 0;
            }
            return _actualMemory.FindSignature(signature, rangeStart, rangeEnd, process);
        }

        public void ThrowIfNotInGame()
        {
            if (_actualMemory == null)
            {
                LoneLogging.WriteLine($"[SafeMode] ThrowIfNotInGame skipped - assuming not in game");
                return;
            }
            _actualMemory.ThrowIfNotInGame();
        }

        public Rectangle GetMonitorRes()
        {
            if (_actualMemory == null)
            {
                LoneLogging.WriteLine($"[SafeMode] GetMonitorRes returning default resolution");
                return new Rectangle(0, 0, 1920, 1080);
            }
            return _actualMemory.GetMonitorRes();
        }

        public ulong GetCodeCave()
        {
            if (_actualMemory == null)
            {
                LoneLogging.WriteLine($"[SafeMode] GetCodeCave skipped");
                return 0;
            }
            return _actualMemory.GetCodeCave();
        }

        public void CloseFPGA()
        {
            if (_actualMemory == null)
            {
                LoneLogging.WriteLine($"[SafeMode] CloseFPGA skipped");
                return;
            }
            _actualMemory.CloseFPGA();
        }
    }
}