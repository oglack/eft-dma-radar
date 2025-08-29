using System;
using System.Numerics;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_shared.Common.DMA;
using eft_dma_shared.Common.DMA.ScatterAPI;
using eft_dma_shared.Common.Misc;
using eft_dma_shared.Common.Unity;
using eft_dma_shared.Common.Unity.LowLevel;
using eft_dma_shared.Common.Unity.LowLevel.Hooks;
using static eft_dma_shared.Common.Unity.MonoLib;
using eft_dma_shared.Common.Features;
using eft_dma_shared.Common.Misc.Pools;
using eft_dma_radar.UI.Misc;
using static eft_dma_radar.Tarkov.EFTPlayer.Player;

namespace eft_dma_radar.Tarkov.Features.MemoryWrites.Patches
{
    public sealed class VisibilityLinecast : MemPatchFeature<VisibilityLinecast>
    {
        private static ulong _compiledMethod;
        private static ulong _gameworldUpdate;
        private static ulong _transformGetPos;
        private const int HitInfoSize = 64;
        private static int LayerMask;
        private static readonly byte[] _zeroBuffer = new byte[HitInfoSize];

        [ThreadStatic] private static ulong _startVectorAddr;
        [ThreadStatic] private static ulong _endVectorAddr;
        [ThreadStatic] private static ulong _hitInfoAddr;

        public static bool Initilized { get; private set; } = false;

        public override bool Enabled
        {
            get => MemWrites.Config.VisCheck.Enabled;
            set => MemWrites.Config.VisCheck.Enabled = value;
        }
        //public static void InitVisChell()
        //{
        //    var offsets = new VisibilityHook.VisibilityCheckOffsets(SharedSDK.SharedOffsets.Player._playerBody);
        //
        //    VisibilityHook.Initialize(
        //        "VisCheck.dll",
        //        "VisCheck.pdb",
        //        LayerMask,
        //        _gameworldUpdate,
        //        _compiledMethod,
        //        _transformGetPos,
        //        offsets // ⬅️ This now includes all needed offsets
        //    );          
        //}

        public override bool TryApply()
        {
            if (!Enabled || !CanRun || !NativeHook.Initialized || !Memory.InRaid || Initilized || !MemWrites.Config.VisCheck.Enabled)
                return false;

            try
            {
                if (_compiledMethod == 0)
                {
                    var physicsClass = MonoClass.Find("UnityEngine.PhysicsModule", "UnityEngine.Physics", out ulong classAddr);
                    if (classAddr == 0x0) return false;

                    NativeMethods.CompileClass(classAddr);
                    var method = physicsClass.FindMethod("Linecast", 4);
                    if (method == 0x0) return false;

                    _compiledMethod = NativeMethods.CompileMethod(method);
                    if (_compiledMethod == 0x0) return false;
                }
                //if (_gameworldUpdate == 0)
                //{
                //    const string className = "EFT.GameWorld";
                //    const string methodName = "Update";
                //
                //    var fClass = MonoLib.MonoClass.Find("Assembly-CSharp", className, out _);
                //
                //    var fMethod = fClass.FindMethod(methodName);
                //    if (!fMethod.IsValidVirtualAddress())
                //        throw new Exception($"Unable to find {className}:{methodName}()");
                //
                //    _gameworldUpdate = NativeMethods.CompileMethod(fMethod);
                //    if (!_gameworldUpdate.IsValidVirtualAddress())
                //        throw new Exception($"Unable to compile {className}:{methodName}()");                    
                //}
                //if (_transformGetPos == 0)
                //{
                //    const string transformClass = "UnityEngine.Transform";
                //    var tClass = MonoLib.MonoClass.Find("UnityEngine.CoreModule", transformClass, out ulong tClassAddr);
                //    NativeMethods.CompileClass(tClassAddr);
                //
                //    // GetPosition
                //    var getPosMethod = tClass.FindMethod("get_position");
                //    if (!getPosMethod.IsValidVirtualAddress())
                //        throw new Exception("Unable to find Transform:get_position()");
                //    _transformGetPos = NativeMethods.CompileMethod(getPosMethod);
                //    if (!_transformGetPos.IsValidVirtualAddress())
                //        throw new Exception("Unable to compile Transform:get_position()");                    
                //}
                Initilized = true;
                LoneLogging.WriteLine("[Visibility Check] Initialized successfully.");
                return true;
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[VisibilityLinecast] ERROR in Initialize: {ex}");
                return false;
            }
        }

        public static bool IsVisible(Vector3 rayStart, Vector3 rayEnd)
        {
            try
            {
                if (!Initilized || _compiledMethod == 0)
                    return false;

                if (_startVectorAddr == 0)
                {
                    _startVectorAddr = NativeMethods.AllocBytes(16);
                    _endVectorAddr = NativeMethods.AllocBytes(16);
                    _hitInfoAddr = NativeMethods.AllocBytes(HitInfoSize);

                    if (_startVectorAddr == 0 || _endVectorAddr == 0 || _hitInfoAddr == 0)
                    {
                        LoneLogging.WriteLine("[VisibilityLinecast] ERROR: Failed to allocate scratch memory!");
                        return false;
                    }
                }

                Memory.WriteValue(_startVectorAddr, ref rayStart);
                Memory.WriteValue(_endVectorAddr, ref rayEnd);
                LayerMask = 6144;
                Memory.WriteBuffer<byte>(_hitInfoAddr, _zeroBuffer);

                ulong? result = NativeHook.Call(
                    _compiledMethod,
                    _startVectorAddr,
                    _endVectorAddr,
                    _hitInfoAddr,
                    (ulong)LayerMask
                );

                return result.HasValue && result.Value == 0;
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[VisibilityLinecast] ERROR in IsVisible: {ex}");
                return false;
            }
        }

        public override void OnGameStop()
        {
            _compiledMethod = 0;
            _startVectorAddr = 0;
            _endVectorAddr = 0;
            _hitInfoAddr = 0;
            Initilized = false;
        }
        public override void OnRaidEnd()
        {
            _compiledMethod = 0;
            _startVectorAddr = 0;
            _endVectorAddr = 0;
            _hitInfoAddr = 0;
            Initilized = false;
            base.OnRaidEnd();
        }
    }

    public struct PlayerVisibilityData
    {
        public Player Player;
        public float Distance;
        public float ViewAngle;
        public int Priority;
        public long LastUpdateTime;
        public int BoneCount;
        public int UpdateInterval;
    }

    public sealed class VisibilityManager
    {
        private float MAX_CHECK_DISTANCE = MemWrites.Config.VisCheck.FarDist;

        // Use configurable ranges from the config class
        private static float CLOSE_RANGE = MemWrites.Config.VisCheck.LowDist;  // From config
        private static float MID_RANGE = MemWrites.Config.VisCheck.MidDist;      // From config
        private static float FAR_RANGE = MemWrites.Config.VisCheck.FarDist;      // From config

        // Update intervals in milliseconds based on priority
        private const int HIGH_PRIORITY_INTERVAL = 8;   // Very close or looking at
        private const int MID_PRIORITY_INTERVAL = 32;   // Medium range
        private const int LOW_PRIORITY_INTERVAL = 150;   // Far range

        private static readonly Bones[] KeyBones = new Bones[]
        {
            Bones.HumanNeck,        // Most important - head visibility
            Bones.HumanSpine2,      // Upper torso
            Bones.HumanPelvis,      // Lower torso
            Bones.HumanLForearm2,   // Left arm
            Bones.HumanRForearm2,   // Right arm
            Bones.HumanLThigh2,     // Left leg
            Bones.HumanRThigh2      // Right leg
        };

        private static readonly Dictionary<Bones, Bones[]> VisibilityInheritance = new()
        {
            { Bones.HumanNeck, new[] { Bones.HumanHead } },
            { Bones.HumanSpine2, new[] { Bones.HumanSpine3, Bones.HumanSpine1 } },
            { Bones.HumanLForearm2, new[] { Bones.HumanLCollarbone, Bones.HumanLPalm } },
            { Bones.HumanRForearm2, new[] { Bones.HumanRCollarbone, Bones.HumanRPalm } },
            { Bones.HumanLThigh2, new[] { Bones.HumanLFoot } },
            { Bones.HumanRThigh2, new[] { Bones.HumanRFoot } }
        };

        private static readonly Dictionary<int, PlayerVisibilityData> _playerData = new();

        private static float CalculateViewAngle(Vector3 fireportPos, Vector3 fireportDirection, Vector3 targetPos)
        {
            var toTarget = Vector3.Normalize(targetPos - fireportPos);
            var dot = Vector3.Dot(fireportDirection, toTarget);
            return MathF.Acos(MathF.Max(-1f, MathF.Min(1f, dot))) * 180f / MathF.PI;
        }

        private static PlayerVisibilityData CalculatePlayerPriority(
            Player player, Vector3 fireportPos, Vector3 fireportDirection, long currentTime, Player targetedPlayer)
        {
            float distance = Vector3.Distance(fireportPos, player.Position);
            float viewAngle = CalculateViewAngle(fireportPos, fireportDirection, player.Position);
            bool isAi = player.IsAI;
            bool wasVisible = player.IsVisible; // ⬅️ NEW

            int priority;
            int boneCount;
            int updateInterval;

            if (player == targetedPlayer)
            {
                priority = 0;
                boneCount = KeyBones.Length;
                updateInterval = HIGH_PRIORITY_INTERVAL;
            }
            else if (wasVisible) // ⬅️ NEW: previously visible => recheck fast, with full bones
            {
                priority = 1;
                boneCount = KeyBones.Length;
                updateInterval = HIGH_PRIORITY_INTERVAL; // ~8ms cadence
            }
            else if (isAi)
            {
                priority = 4;
                boneCount = 2;
                updateInterval = LOW_PRIORITY_INTERVAL;
            }
            else if (distance <= CLOSE_RANGE)
            {
                priority = 1;
                boneCount = KeyBones.Length;
                updateInterval = HIGH_PRIORITY_INTERVAL;
            }
            else if (distance <= MID_RANGE)
            {
                priority = 2;
                boneCount = 4;
                updateInterval = MID_PRIORITY_INTERVAL;
            }
            else
            {
                priority = 3;
                boneCount = 2;
                updateInterval = LOW_PRIORITY_INTERVAL;
            }

            return new PlayerVisibilityData
            {
                Player = player,
                Distance = distance,
                ViewAngle = viewAngle,
                Priority = priority,
                LastUpdateTime = currentTime,   // you already had this
                BoneCount = boneCount,
                UpdateInterval = updateInterval
            };
        }

        public static void UpdateVisibilityForPlayers(IEnumerable<Player> players, Vector3 fireportPos, Vector3 fireportDirection = default)
        {
            try
            {
                if (!VisibilityLinecast.Initilized || players == null)
                    return;

                long currentTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                // If no direction provided, use forward vector
                if (fireportDirection == default)
                    fireportDirection = Vector3.UnitZ;

                var playersToUpdate = new List<PlayerVisibilityData>();
                Player targetedPlayer = null;

                // First pass: identify targeted player and calculate priorities
                foreach (var player in players)
                {
                    try
                    {
                        if (player == null || !player.IsActive || !player.IsAlive || player is LocalPlayer || player.Skeleton?.Bones == null)
                            continue;
                        if (MemWrites.Config.VisCheck.IgnoreAi && player.Type is not PlayerType.AIBoss)
                        {
                            if (player.Type is PlayerType.AIScav)
                                continue;
                        }
                        // Check if this is our targeted player
                        if (player.IsAimbotLocked)
                        {
                            targetedPlayer = player;
                        }

                        var playerData = CalculatePlayerPriority(player, fireportPos, fireportDirection, currentTime, targetedPlayer);

                        if (playerData.Distance > MemWrites.Config.VisCheck.FarDist)
                        {
                            player.IsVisible = false;
                            continue;
                        }

                        int playerId = player.GetHashCode();

                        if (_playerData.TryGetValue(playerId, out var existingData))
                        {
                            if (currentTime - existingData.LastUpdateTime < existingData.UpdateInterval)
                                continue;
                        }

                        _playerData[playerId] = playerData;
                        playersToUpdate.Add(playerData);
                    }
                    catch (Exception ex)
                    {
                        LoneLogging.WriteLine($"[VisCheck] Player priority calculation error: {ex}");
                    }
                }

                // If we have a targeted player, prioritize them above all others
                if (targetedPlayer != null)
                {
                    // Remove targeted player from regular update list if present
                    playersToUpdate.RemoveAll(p => p.Player == targetedPlayer);

                    // Create high priority data for targeted player
                    var targetedData = new PlayerVisibilityData
                    {
                        Player = targetedPlayer,
                        Distance = Vector3.Distance(fireportPos, targetedPlayer.Position),
                        ViewAngle = CalculateViewAngle(fireportPos, fireportDirection, targetedPlayer.Position),
                        Priority = 0, // Highest priority
                        LastUpdateTime = currentTime,
                        BoneCount = KeyBones.Length, // Check all bones
                        UpdateInterval = HIGH_PRIORITY_INTERVAL // Update every frame
                    };

                    // Insert at beginning of list
                    playersToUpdate.Insert(0, targetedData);
                    _playerData[targetedPlayer.GetHashCode()] = targetedData;
                }

                // Sort remaining players by priority (1 = highest, 4 = lowest), then by distance
                playersToUpdate.Sort((a, b) =>
                {
                    bool av = a.Player.IsVisible;
                    bool bv = b.Player.IsVisible;
                    if (av != bv) return av ? -1 : 1;  // ⬅️ NEW: visible first

                    if (a.Priority == b.Priority)
                        return a.Distance.CompareTo(b.Distance);
                    return a.Priority.CompareTo(b.Priority);
                });


                // Process players in batches - give targeted player full attention
                int maxPlayersPerFrame = targetedPlayer != null ? 4 : 8;
                var playersThisFrame = playersToUpdate.Take(maxPlayersPerFrame).ToList();

                if (playersThisFrame.Count == 0)
                    return;

                using var map = ScatterReadMap.Get();

                foreach (var playerData in playersThisFrame)
                {
                    try
                    {
                        var player = playerData.Player;
                        var round = map.AddRound();
                        var index = round[0];

                        var actualIndexes = new List<int>();
                        var usedBones = new List<Bones>();

                        // For targeted player, check all bones
                        int boneCount = (player.IsAimbotLocked)
                            ? KeyBones.Length
                            : playerData.BoneCount;

                        for (int i = 0; i < boneCount && i < KeyBones.Length; i++)
                        {
                            if (player.Skeleton.Bones.TryGetValue(KeyBones[i], out var transform) && transform != null)
                            {
                                index.AddEntry<SharedArray<UnityTransform.TrsX>>(
                                    i,
                                    transform.VerticesAddr,
                                    (3 * transform.Index + 3) * 16
                                );
                                actualIndexes.Add(i);
                                usedBones.Add(KeyBones[i]);
                            }
                        }

                        var capturedPlayer = player;
                        var capturedBones = usedBones.ToArray();

                        index.Callbacks += x =>
                        {
                            try
                            {
                                var results = new bool[actualIndexes.Count];
                                bool anyVisible = false;

                                for (int i = 0; i < actualIndexes.Count; i++)
                                {
                                    int idx = actualIndexes[i];
                                    if (x.TryGetResult<SharedArray<UnityTransform.TrsX>>(idx, out var vertices))
                                    {
                                        if (capturedPlayer.Skeleton.Bones.TryGetValue(capturedBones[i], out var transform) && transform != null)
                                        {
                                            transform.UpdatePosition(vertices);
                                            results[i] = VisibilityLinecast.IsVisible(fireportPos, transform.Position);
                                            anyVisible |= results[i];
                                        }
                                    }
                                }

                                capturedPlayer.UpdateBoneVisibility(capturedBones, results);

                                // Apply inheritance for visible bones
                                for (int i = 0; i < capturedBones.Length; i++)
                                {
                                    if (!results[i]) continue;

                                    if (VisibilityInheritance.TryGetValue(capturedBones[i], out var inferredBones))
                                    {
                                        foreach (var inferred in inferredBones)
                                            capturedPlayer.BoneVisibility[inferred] = true;
                                    }
                                }

                                capturedPlayer.IsVisible = anyVisible;
                                try
                                {
                                    // bump the time so the interval gating is accurate
                                    int id = capturedPlayer.GetHashCode();
                                    if (_playerData.TryGetValue(id, out var pd))
                                    {
                                        pd.LastUpdateTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); // ⬅️ NEW
                                        _playerData[id] = pd;                                               // ⬅️ NEW
                                    }
                                }
                                catch { /* ignore */ }                                
                            }
                            catch (Exception ex)
                            {
                                LoneLogging.WriteLine($"[VisCheck] Error in callback for {capturedPlayer.Name}: {ex}");
                                capturedPlayer.IsVisible = false;
                            }
                        };
                    }
                    catch (Exception ex)
                    {
                        LoneLogging.WriteLine($"[VisCheck] Player processing error: {ex}");
                    }
                }

                map.Execute();

                // Clean up old player data periodically
                if (currentTime % 5000 == 0) // Every 5 seconds
                {
                    var keysToRemove = new List<int>();
                    foreach (var kvp in _playerData)
                    {
                        if (currentTime - kvp.Value.LastUpdateTime > 10000) // 10 seconds old
                            keysToRemove.Add(kvp.Key);
                    }
                    foreach (var key in keysToRemove)
                        _playerData.Remove(key);
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[VisCheck] System error: {ex}");
            }
        }
    }
}