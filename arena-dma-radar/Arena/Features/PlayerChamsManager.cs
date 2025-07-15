using arena_dma_radar.Arena.ArenaPlayer;
using arena_dma_radar.Arena.GameWorld;
using arena_dma_radar.UI.Misc;
using eft_dma_shared.Common.DMA.ScatterAPI;
using eft_dma_shared.Common.Misc;
using eft_dma_shared.Common.Misc.Config;
using eft_dma_shared.Common.Unity;
using eft_dma_shared.Common.Unity.Collections;
using eft_dma_shared.Common.Unity.LowLevel;
using eft_dma_shared.Common.Unity.LowLevel.Chams;
using eft_dma_shared.Common.Unity.LowLevel.Chams.Arena;
using eft_dma_shared.Common.Unity.LowLevel.Hooks;
using eft_dma_shared.Common.Unity.LowLevel.Types;
using System.Collections.Concurrent;

namespace arena_dma_radar.Arena.Features
{
    /// <summary>
    /// Manages player chams application, caching, and restoration
    /// </summary>
    public static class PlayerChamsManager
    {
        private static readonly ConcurrentDictionary<ulong, PlayerChamsState> _playerStates = new();
        private static readonly ConcurrentDictionary<ulong, CachedPlayerMaterials> _cachedMaterials = new();
        private static readonly ConcurrentDictionary<ulong, DateTime> _playerDeathTimes = new();

        private static Config Config => Program.Config;
        private static ChamsConfig ChamsConfig => Config.ChamsConfig;

        #region Public API

        public static void ProcessPlayerChams(ScatterWriteHandle writes, LocalGameWorld game)
        {
            try
            {
                if (!ChamsConfig.Enabled)
                {
                    RevertAllPlayerChams(writes, game);
                    return;
                }

                var activePlayers = game.Players.Where(x => x.IsHostileActive || x.Type == Player.PlayerType.Teammate).ToList();
                if (!activePlayers.Any())
                    return;

                foreach (var player in activePlayers)
                {
                    if (!ShouldProcessPlayer(player))
                        continue;

                    var entityType = GetEntityType(player);
                    var entitySettings = ChamsConfig.GetEntitySettings(entityType);

                    if (!entitySettings.Enabled || !AreMaterialsReady(entitySettings, entityType))
                        continue;

                    ApplyChamsToPlayer(writes, game, player, entitySettings);
                }

                ProcessDeathReverts(writes, game);
                CleanupInactivePlayers(game.Players.Select(p => p.Base).ToHashSet());
                SaveCache();
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[Player Chams] Error processing: {ex.Message}");
            }
        }

        public static void ApplyAimbotChams(Player player, LocalGameWorld game)
        {
            if (!ChamsConfig.Enabled || player == null || !player.IsActive || !player.IsAlive)
                return;

            try
            {
                var state = GetOrCreateState(player.Base);
                if (state.IsAimbotTarget && state.IsActive)
                    return;

                var aimbotSettings = ChamsConfig.GetEntitySettings(ChamsEntityType.AimbotTarget);
                var clothingMaterialId = GetMaterialId(aimbotSettings.ClothingChamsMode, game.CameraManager, ChamsEntityType.AimbotTarget, aimbotSettings);
                var gearMaterialId = GetMaterialId(aimbotSettings.GearChamsMode, game.CameraManager, ChamsEntityType.AimbotTarget, aimbotSettings);

                var primaryMaterialId = clothingMaterialId != -1 ? clothingMaterialId : gearMaterialId;
                var secondaryMaterialId = gearMaterialId != -1 ? gearMaterialId : clothingMaterialId;

                if (primaryMaterialId == -1 && secondaryMaterialId == -1)
                    return;

                CachePlayerMaterials(player);

                var writeHandle = new ScatterWriteHandle();

                ApplyChamsInternal(writeHandle, player, aimbotSettings, clothingMaterialId, gearMaterialId);

                writeHandle.Execute(() => ValidateWrite(player, game));

                UpdateStateForAimbot(player, aimbotSettings.ClothingChamsMode, aimbotSettings.GearChamsMode, clothingMaterialId, gearMaterialId);

                LoneLogging.WriteLine($"[Player Chams] Applied aimbot chams (Clothing: {aimbotSettings.ClothingChamsMode}, Gear: {aimbotSettings.GearChamsMode}) to {player.Name}");
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[Player Chams] Failed to apply aimbot chams to {player.Name}: {ex.Message}");
            }
        }

        public static void RemoveAimbotChams(Player player, LocalGameWorld game, bool revertToNormalChams = true)
        {
            if (!ChamsConfig.Enabled || player == null || !player.IsActive || !player.IsAlive)
                return;

            try
            {
                var state = GetState(player.Base);
                if (state == null || !state.IsAimbotTarget)
                    return;

                state.IsAimbotTarget = false;
                state.IsActive = false;

                LoneLogging.WriteLine($"[Player Chams] Removed aimbot chams from {player.Name}");

                if (revertToNormalChams)
                {
                    var entityType = GetEntityType(player);
                    var entitySettings = ChamsConfig.GetEntitySettings(entityType);

                    if (entitySettings.Enabled)
                    {
                        var writeHandle = new ScatterWriteHandle();
                        ApplyChamsToPlayer(writeHandle, game, player, entitySettings);
                    }
                    else
                    {
                        RevertPlayerChams(player.Base, game);
                    }
                }
                else
                {
                    RevertPlayerChams(player.Base, game);
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[Player Chams] Failed to remove aimbot chams from {player.Name}: {ex.Message}");
            }
        }

        public static void ApplyDeathMaterial(Player player, LocalGameWorld game)
        {
            try
            {
                if (!ChamsConfig.Enabled)
                    return;

                var state = GetState(player.Base);
                if (state?.HasDeathMaterialApplied == true)
                    return;

                var entityType = GetEntityType(player);
                var entitySettings = ChamsConfig.GetEntitySettings(entityType);

                if (!entitySettings.DeathMaterialEnabled)
                    return;

                var deathMaterialId = GetMaterialId(entitySettings.DeathMaterialMode, game.CameraManager, entityType, entitySettings);
                if (deathMaterialId == -1)
                    return;

                var writeHandle = new ScatterWriteHandle();
                ApplyChamsInternal(writeHandle, player, entitySettings.DeathMaterialMode, deathMaterialId, deathMaterialId);
                writeHandle.Execute(() => game.IsSafeToWriteMem);

                if (state != null)
                {
                    state.HasDeathMaterialApplied = true;
                    state.IsActive = false;
                }

                LoneLogging.WriteLine($"[Player Chams] Applied death material to {player.Name}");
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[Player Chams] Failed to apply death material to {player.Name}: {ex.Message}");
            }
        }

        public static void RevertAllPlayerChams(ScatterWriteHandle writes, LocalGameWorld game)
        {
            var playersToRevert = _playerStates.Keys.ToList();
            foreach (var playerBase in playersToRevert)
            {
                RevertPlayerChams(playerBase, game);
            }

            Reset();
        }

        public static void Reset()
        {
            ChamsManager.MaterialsUpdated -= OnMaterialsUpdated;

            _playerStates.Clear();
            _cachedMaterials.Clear();
            _playerDeathTimes.Clear();
        }

        public static void Initialize()
        {
            LoadCache();
            ApplyConfiguredColors();

            ChamsManager.MaterialsUpdated += OnMaterialsUpdated;

            LoneLogging.WriteLine("[Player Chams] Manager initialized");
        }

        private static void OnMaterialsUpdated()
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(200);
                    ApplyConfiguredColors();
                    LoneLogging.WriteLine("[Player Chams] Applied colors after materials update");
                }
                catch (Exception ex)
                {
                    LoneLogging.WriteLine($"[Player Chams] Error applying colors after materials update: {ex.Message}");
                }
            });
        }

        #endregion

        #region Private Implementation

        private static bool ShouldProcessPlayer(Player player)
        {
            if (player.IsAimbotLocked || !player.IsActive || !player.IsAlive)
                return false;

            var state = GetState(player.Base);
            return state?.IsAimbotTarget != true || !state.IsActive;
        }

        private static bool AreMaterialsReady(ChamsConfig.EntityChamsSettings entitySettings, ChamsEntityType entityType)
        {
            var clothingReady = !entitySettings.ClothingChamsEnabled ||
                IsBasicMode(entitySettings.ClothingChamsMode) ||
                ChamsManager.AreMaterialsReadyForEntityType(entityType);

            var gearReady = !entitySettings.GearChamsEnabled ||
                IsBasicMode(entitySettings.GearChamsMode) ||
                ChamsManager.AreMaterialsReadyForEntityType(entityType);

            return clothingReady && gearReady;
        }

        private static bool IsBasicMode(ChamsMode mode) => mode == ChamsMode.Basic || mode == ChamsMode.Visible;

        private static void ApplyChamsToPlayer(ScatterWriteHandle writes, LocalGameWorld game, Player player, ChamsConfig.EntityChamsSettings entitySettings)
        {
            try
            {
                var state = GetOrCreateState(player.Base);

                var clothingMaterialId = GetClothingMaterialId(entitySettings, game.CameraManager, player);
                var gearMaterialId = GetGearMaterialId(entitySettings, game.CameraManager, player);

                bool needsUpdate = StateNeedsUpdate(state, entitySettings, clothingMaterialId, gearMaterialId);

                if (!needsUpdate)
                    return;

                CachePlayerMaterials(player);

                writes.Clear();
                ApplyChamsInternal(writes, player, entitySettings, clothingMaterialId, gearMaterialId);
                writes.Execute(() => ValidateWrite(player, game));

                UpdateState(state, entitySettings, clothingMaterialId, gearMaterialId);

                LoneLogging.WriteLine($"[Player Chams] Applied chams to {player.Name} - Clothing: {entitySettings.ClothingChamsMode}, Gear: {entitySettings.GearChamsMode}");
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[Player Chams] Failed to apply chams to {player.Name}: {ex.Message}");
            }
        }

        private static void UpdateStateForAimbot(Player player, ChamsMode clothingMode, ChamsMode gearMode, int clothingMaterialId, int gearMaterialId)
        {
            var state = GetOrCreateState(player.Base);
            state.ClothingMode = clothingMode;
            state.GearMode = gearMode;
            state.ClothingMaterialId = clothingMaterialId;
            state.GearMaterialId = gearMaterialId;
            state.LastAppliedTime = DateTime.UtcNow;
            state.IsActive = true;
            state.IsAimbotTarget = true;
        }

        private static bool StateNeedsUpdate(PlayerChamsState state, ChamsConfig.EntityChamsSettings entitySettings, int clothingMaterialId, int gearMaterialId)
        {
            return state.ClothingMode != entitySettings.ClothingChamsMode ||
                   state.GearMode != entitySettings.GearChamsMode ||
                   state.ClothingMaterialId != clothingMaterialId ||
                   state.GearMaterialId != gearMaterialId;
        }

        private static void ApplyChamsInternal(ScatterWriteHandle writes, Player player, ChamsConfig.EntityChamsSettings entitySettings, int clothingMaterialId, int gearMaterialId)
        {
            if (entitySettings.ClothingChamsEnabled && clothingMaterialId != -1)
                ApplyClothingChams(writes, player, clothingMaterialId);

            if (entitySettings.GearChamsEnabled && gearMaterialId != -1)
                ApplyGearChams(writes, player, gearMaterialId);
        }

        private static void ApplyChamsInternal(ScatterWriteHandle writes, Player player, ChamsMode mode, int clothingMaterialId, int gearMaterialId)
        {
            ApplyClothingChams(writes, player, clothingMaterialId);
            ApplyGearChams(writes, player, gearMaterialId);
        }

        private static void ApplyClothingChams(ScatterWriteHandle writes, Player player, int materialId)
        {
            var pRendererContainersArray = Memory.ReadPtr(player.Body + Offsets.PlayerBody._bodyRenderers);
            using var rendererContainersArray = MemArray<Types.BodyRendererContainer>.Get(pRendererContainersArray);

            foreach (var rendererContainer in rendererContainersArray)
            {
                using var renderersArray = MemArray<ulong>.Get(rendererContainer.Renderers);

                foreach (var skinnedMeshRenderer in renderersArray)
                {
                    var renderer = Memory.ReadPtr(skinnedMeshRenderer + UnityOffsets.SkinnedMeshRenderer.Renderer);
                    WriteMaterialToRenderer(writes, renderer, materialId);
                }
            }
        }

        private static void ApplyGearChams(ScatterWriteHandle writes, Player player, int materialId)
        {
            var slotViews = Memory.ReadValue<ulong>(player.Body + Offsets.PlayerBody.SlotViews);
            if (!Utils.IsValidVirtualAddress(slotViews))
                return;

            var pSlotViewsDict = Memory.ReadValue<ulong>(slotViews + Offsets.SlotViewsContainer.Dict);
            if (!Utils.IsValidVirtualAddress(pSlotViewsDict))
                return;

            using var slotViewsDict = MemDictionary<ulong, ulong>.Get(pSlotViewsDict);

            foreach (var slot in slotViewsDict)
            {
                if (!Utils.IsValidVirtualAddress(slot.Value))
                    continue;

                ProcessSlotDresses(writes, slot.Value, materialId);
            }
        }

        private static void ProcessSlotDresses(ScatterWriteHandle writes, ulong slotValue, int materialId)
        {
            var pDressesArray = Memory.ReadValue<ulong>(slotValue + Offsets.PlayerBodySubclass.Dresses);
            if (!Utils.IsValidVirtualAddress(pDressesArray))
                return;

            using var dressesArray = MemArray<ulong>.Get(pDressesArray);

            foreach (var dress in dressesArray)
            {
                if (!Utils.IsValidVirtualAddress(dress))
                    continue;

                var pRenderersArray = Memory.ReadValue<ulong>(dress + Offsets.Dress.Renderers);
                if (!Utils.IsValidVirtualAddress(pRenderersArray))
                    continue;

                using var renderersArray = MemArray<ulong>.Get(pRenderersArray);

                foreach (var renderer in renderersArray)
                {
                    if (!Utils.IsValidVirtualAddress(renderer))
                        continue;

                    var rendererNative = Memory.ReadValue<ulong>(renderer + 0x10);
                    if (Utils.IsValidVirtualAddress(rendererNative))
                        WriteMaterialToRenderer(writes, rendererNative, materialId);
                }
            }
        }

        private static void WriteMaterialToRenderer(ScatterWriteHandle writes, ulong renderer, int materialId)
        {
            try
            {
                int materialsCount = Memory.ReadValueEnsure<int>(renderer + UnityOffsets.Renderer.Count);
                if (materialsCount <= 0 || materialsCount > 30)
                    return;

                var materialsArrayPtr = Memory.ReadValueEnsure<ulong>(renderer + UnityOffsets.Renderer.Materials);
                materialsArrayPtr.ThrowIfInvalidVirtualAddress();

                var materials = Enumerable.Repeat(materialId, materialsCount).ToArray();
                writes.AddBufferEntry(materialsArrayPtr, materials.AsSpan());
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[Player Chams] Failed to write material to renderer: {ex.Message}");
            }
        }

        private static bool ValidateWrite(Player player, LocalGameWorld game)
        {
            if (Memory.ReadValue<ulong>(player.CorpseAddr, false) != 0)
                return false;
            return game.IsSafeToWriteMem;
        }

        #endregion

        #region Material Management

        private static int GetClothingMaterialId(ChamsConfig.EntityChamsSettings entitySettings, CameraManager cameraManager, Player player)
        {
            if (!entitySettings.ClothingChamsEnabled)
                return -1;

            var entityType = GetEntityType(player);
            return GetMaterialId(entitySettings.ClothingChamsMode, cameraManager, entityType, entitySettings);
        }

        private static int GetGearMaterialId(ChamsConfig.EntityChamsSettings entitySettings, CameraManager cameraManager, Player player)
        {
            if (!entitySettings.GearChamsEnabled)
                return -1;

            var entityType = GetEntityType(player);
            return GetMaterialId(entitySettings.GearChamsMode, cameraManager, entityType, entitySettings);
        }

        private static int GetMaterialId(ChamsMode mode, CameraManager cameraManager, ChamsEntityType entityType, ChamsConfig.EntityChamsSettings settings)
        {
            return mode switch
            {
                ChamsMode.Basic => GetBasicMaterialId(cameraManager),
                ChamsMode.Visible => GetVisibleMaterialId(cameraManager, settings),
                _ => ChamsManager.GetMaterialIDForPlayer(mode, entityType)
            };
        }

        private static int GetBasicMaterialId(CameraManager cameraManager)
        {
            try
            {
                var ssaa = MonoBehaviour.GetComponent(cameraManager.FPSCamera, "SSAA");
                if (ssaa == 0) return -1;

                var opticMaskMaterial = Memory.ReadPtr(ssaa + UnityOffsets.SSAA.OpticMaskMaterial);
                if (opticMaskMaterial == 0) return -1;

                var opticMonoBehaviour = Memory.ReadPtr(opticMaskMaterial + ObjectClass.MonoBehaviourOffset);
                if (opticMonoBehaviour == 0) return -1;

                return Memory.ReadValue<MonoBehaviour>(opticMonoBehaviour).InstanceID;
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[Player Chams] Failed to get basic material ID: {ex.Message}");
                return -1;
            }
        }

        private static int GetVisibleMaterialId(CameraManager cameraManager, ChamsConfig.EntityChamsSettings settings)
        {
            try
            {
                var nvgComponent = MonoBehaviour.GetComponent(cameraManager.FPSCamera, "NightVision");
                if (nvgComponent == 0) return -1;

                var opticMaskMaterial = Memory.ReadPtr(nvgComponent + 0x98);
                if (opticMaskMaterial == 0) return -1;

                var opticMonoBehaviour = Memory.ReadPtr(opticMaskMaterial + ObjectClass.MonoBehaviourOffset);
                if (opticMonoBehaviour == 0) return -1;

                var materialId = Memory.ReadValue<MonoBehaviour>(opticMonoBehaviour).InstanceID;

                if (materialId != -1)
                {
                    var colorAddr = nvgComponent + 0xE0;
                    var unityColor = new UnityColor(settings.VisibleColor);
                    Memory.WriteValue(colorAddr, unityColor);
                }

                return materialId;
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[Player Chams] Failed to get visible material ID: {ex.Message}");
                return -1;
            }
        }

        #endregion

        #region Color Management

        public static void ApplyConfiguredColors()
        {
            try
            {
                if (!ChamsConfig.Enabled)
                    return;

                LoneLogging.WriteLine("[Player Chams] Applying configured colors to materials...");

                using var chamsColorMem = new RemoteBytes(SizeChecker<UnityColor>.Size);
                var colorsApplied = 0;

                foreach (var entityKvp in ChamsConfig.EntityChams)
                {
                    var entityType = entityKvp.Key;
                    var entitySettings = entityKvp.Value;

                    if (!ChamsManager.IsPlayerEntityType(entityType))
                        continue;

                    foreach (var materialKvp in ChamsManager.Materials)
                    {
                        var (mode, matEntityType) = materialKvp.Key;
                        var material = materialKvp.Value;

                        if (matEntityType != entityType || material.InstanceID == 0)
                            continue;

                        try
                        {
                            SKColor visibleColor, invisibleColor;
                            var materialColorSettings = entitySettings.MaterialColors?.ContainsKey(mode) == true
                                ? entitySettings.MaterialColors[mode]
                                : null;

                            if (materialColorSettings != null)
                            {
                                if (!SKColor.TryParse(materialColorSettings.VisibleColor, out visibleColor))
                                    visibleColor = SKColor.Parse("#00FF00");

                                if (!SKColor.TryParse(materialColorSettings.InvisibleColor, out invisibleColor))
                                    invisibleColor = SKColor.Parse("#FF0000");
                            }
                            else
                            {
                                if (!SKColor.TryParse(entitySettings.VisibleColor, out visibleColor))
                                    visibleColor = SKColor.Parse("#00FF00");

                                if (!SKColor.TryParse(entitySettings.InvisibleColor, out invisibleColor))
                                    invisibleColor = SKColor.Parse("#FF0000");
                            }

                            var visibleUnityColor = new UnityColor(visibleColor.Red, visibleColor.Green, visibleColor.Blue, visibleColor.Alpha);
                            var invisibleUnityColor = new UnityColor(invisibleColor.Red, invisibleColor.Green, invisibleColor.Blue, invisibleColor.Alpha);

                            NativeMethods.SetMaterialColor(chamsColorMem, material.Address, material.ColorVisible, visibleUnityColor);
                            NativeMethods.SetMaterialColor(chamsColorMem, material.Address, material.ColorInvisible, invisibleUnityColor);
                            colorsApplied++;
                        }
                        catch (Exception ex)
                        {
                            LoneLogging.WriteLine($"[Player Chams] Failed to set color for {mode}/{entityType}: {ex.Message}");
                        }
                    }
                }

                LoneLogging.WriteLine($"[Player Chams] Applied colors to {colorsApplied} materials");
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[Player Chams] Failed to apply configured colors: {ex.Message}");
            }
        }

        #endregion

        #region State Management

        private static PlayerChamsState GetOrCreateState(ulong playerBase)
        {
            return _playerStates.GetOrAdd(playerBase, _ => new PlayerChamsState());
        }

        private static PlayerChamsState GetState(ulong playerBase)
        {
            return _playerStates.TryGetValue(playerBase, out var state) ? state : null;
        }

        private static void UpdateState(PlayerChamsState state, ChamsConfig.EntityChamsSettings entitySettings, int clothingMaterialId, int gearMaterialId)
        {
            state.ClothingMode = entitySettings.ClothingChamsMode;
            state.GearMode = entitySettings.GearChamsMode;
            state.ClothingMaterialId = clothingMaterialId;
            state.GearMaterialId = gearMaterialId;
            state.LastAppliedTime = DateTime.UtcNow;
            state.IsActive = true;
            state.IsAimbotTarget = false;
        }

        #endregion

        #region Death and Cleanup

        private static void ProcessDeathReverts(ScatterWriteHandle writes, LocalGameWorld game)
        {
            var currentTime = DateTime.UtcNow;
            var playersToRevert = new List<ulong>();

            foreach (var kvp in _playerStates)
            {
                var playerBase = kvp.Key;
                var state = kvp.Value;

                if (!state.IsActive)
                    continue;

                var player = game.Players.FirstOrDefault(p => p.Base == playerBase);
                if (player == null || !player.IsAlive)
                {
                    if (!_playerDeathTimes.ContainsKey(playerBase))
                        _playerDeathTimes[playerBase] = currentTime;

                    var entityType = GetEntityTypeFromBase(playerBase, game);
                    if (entityType.HasValue)
                    {
                        var entitySettings = ChamsConfig.GetEntitySettings(entityType.Value);

                        if (entitySettings.RevertOnDeath && !entitySettings.DeathMaterialEnabled)
                        {
                            playersToRevert.Add(playerBase);
                        }
                        else if (entitySettings.DeathMaterialEnabled)
                        {
                            state.IsActive = false;
                            LoneLogging.WriteLine($"[Player Chams] Player {playerBase:X} died - keeping death material applied");
                        }
                    }
                }
                else
                {
                    _playerDeathTimes.TryRemove(playerBase, out _);
                }
            }

            foreach (var playerBase in playersToRevert)
                RevertPlayerChams(playerBase, game);
        }

        private static void RevertPlayerChams(ulong playerBase, LocalGameWorld game = null)
        {
            try
            {
                if (!_playerStates.TryGetValue(playerBase, out var state))
                    return;

                if (_cachedMaterials.TryGetValue(playerBase, out var cached))
                    RestorePlayerMaterials(playerBase, cached, game);

                state.IsActive = false;
                _playerDeathTimes.TryRemove(playerBase, out _);

                LoneLogging.WriteLine($"[Player Chams] Reverted chams for player {playerBase:X}");
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[Player Chams] Failed to revert chams for player {playerBase:X}: {ex.Message}");
            }
        }

        private static void RestorePlayerMaterials(ulong playerBase, CachedPlayerMaterials cached, LocalGameWorld game)
        {
            try
            {
                var player = game?.Players.FirstOrDefault(p => p.Base == playerBase);
                if (player == null)
                    return;

                RestoreClothingMaterials(player, cached.ClothingMaterials);
                RestoreGearMaterials(player, cached.GearMaterials);

                LoneLogging.WriteLine($"[Player Chams] Restored materials for {cached.PlayerName}");
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[Player Chams] Failed to restore materials for player {playerBase:X}: {ex.Message}");
            }
        }

        private static void RestoreClothingMaterials(Player player, Dictionary<string, Dictionary<int, int>> clothingMaterials)
        {
            try
            {
                var pRendererContainersArray = Memory.ReadPtr(player.Body + Offsets.PlayerBody._bodyRenderers);
                using var rendererContainersArray = MemArray<Types.BodyRendererContainer>.Get(pRendererContainersArray);

                for (var containerIndex = 0; containerIndex < rendererContainersArray.Count; containerIndex++)
                {
                    var rendererContainer = rendererContainersArray[containerIndex];
                    using var renderersArray = MemArray<ulong>.Get(rendererContainer.Renderers);

                    for (var rendererIndex = 0; rendererIndex < renderersArray.Count; rendererIndex++)
                    {
                        var key = $"clothing_{containerIndex}_{rendererIndex}";
                        if (clothingMaterials.TryGetValue(key, out var materials))
                        {
                            var skinnedMeshRenderer = renderersArray[rendererIndex];
                            var renderer = Memory.ReadPtr(skinnedMeshRenderer + UnityOffsets.SkinnedMeshRenderer.Renderer);
                            RestoreRendererMaterials(renderer, materials);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[Player Chams] Failed to restore clothing materials: {ex.Message}");
            }
        }

        private static void RestoreGearMaterials(Player player, Dictionary<string, Dictionary<int, int>> gearMaterials)
        {
            try
            {
                var slotViews = Memory.ReadValue<ulong>(player.Body + Offsets.PlayerBody.SlotViews);
                if (!Utils.IsValidVirtualAddress(slotViews))
                    return;

                var pSlotViewsDict = Memory.ReadValue<ulong>(slotViews + Offsets.SlotViewsContainer.Dict);
                if (!Utils.IsValidVirtualAddress(pSlotViewsDict))
                    return;

                using var slotViewsDict = MemDictionary<ulong, ulong>.Get(pSlotViewsDict);

                var slotIndex = 0;
                foreach (var slot in slotViewsDict)
                {
                    if (!Utils.IsValidVirtualAddress(slot.Value))
                        continue;

                    var pDressesArray = Memory.ReadValue<ulong>(slot.Value + Offsets.PlayerBodySubclass.Dresses);
                    if (!Utils.IsValidVirtualAddress(pDressesArray))
                        continue;

                    using var dressesArray = MemArray<ulong>.Get(pDressesArray);

                    for (var dressIndex = 0; dressIndex < dressesArray.Count; dressIndex++)
                    {
                        var dress = dressesArray[dressIndex];
                        if (!Utils.IsValidVirtualAddress(dress))
                            continue;

                        var pRenderersArray = Memory.ReadValue<ulong>(dress + Offsets.Dress.Renderers);
                        if (!Utils.IsValidVirtualAddress(pRenderersArray))
                            continue;

                        using var renderersArray = MemArray<ulong>.Get(pRenderersArray);

                        for (var rendererIndex = 0; rendererIndex < renderersArray.Count; rendererIndex++)
                        {
                            var key = $"gear_{slotIndex}_{dressIndex}_{rendererIndex}";
                            if (gearMaterials.TryGetValue(key, out var materials))
                            {
                                var renderer = renderersArray[rendererIndex];
                                if (!Utils.IsValidVirtualAddress(renderer))
                                    continue;

                                var rendererNative = Memory.ReadValue<ulong>(renderer + 0x10);
                                if (Utils.IsValidVirtualAddress(rendererNative))
                                    RestoreRendererMaterials(rendererNative, materials);
                            }
                        }
                    }
                    slotIndex++;
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[Player Chams] Failed to restore gear materials: {ex.Message}");
            }
        }

        private static void RestoreRendererMaterials(ulong renderer, Dictionary<int, int> originalMaterials)
        {
            try
            {
                int materialsCount = Memory.ReadValueEnsure<int>(renderer + UnityOffsets.Renderer.Count);
                if (materialsCount <= 0 || materialsCount > 30)
                    return;

                var materialsArrayPtr = Memory.ReadValueEnsure<ulong>(renderer + UnityOffsets.Renderer.Materials);
                if (!Utils.IsValidVirtualAddress(materialsArrayPtr))
                    return;

                for (int i = 0; i < materialsCount; i++)
                {
                    if (originalMaterials.TryGetValue(i, out var originalMaterial))
                        Memory.WriteValue(materialsArrayPtr + (ulong)(i * 4), originalMaterial);
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[Player Chams] Failed to restore renderer materials: {ex.Message}");
            }
        }

        private static void CleanupInactivePlayers(HashSet<ulong> activePlayerBases)
        {
            var inactivePlayerBases = _playerStates.Keys.Where(playerBase => !activePlayerBases.Contains(playerBase)).ToList();

            foreach (var playerBase in inactivePlayerBases)
            {
                RevertPlayerChams(playerBase);
                _playerStates.TryRemove(playerBase, out _);
                _playerDeathTimes.TryRemove(playerBase, out _);
                _cachedMaterials.TryRemove(playerBase, out _);
            }

            if (inactivePlayerBases.Any())
                LoneLogging.WriteLine($"[Player Chams] Cleaned up {inactivePlayerBases.Count} inactive players");
        }

        #endregion

        #region Caching

        private static void CachePlayerMaterials(Player player)
        {
            try
            {
                if (_cachedMaterials.ContainsKey(player.Base))
                {
                    LoneLogging.WriteLine($"[Player Chams] Materials already cached for {player.Name}");
                    return;
                }

                var clothingMaterials = new Dictionary<string, Dictionary<int, int>>();
                var gearMaterials = new Dictionary<string, Dictionary<int, int>>();

                CacheClothingMaterials(player, clothingMaterials);
                CacheGearMaterials(player, gearMaterials);

                if (clothingMaterials.Count > 0 || gearMaterials.Count > 0)
                {
                    _cachedMaterials[player.Base] = new CachedPlayerMaterials
                    {
                        PlayerBase = player.Base,
                        PlayerName = player.Name,
                        ClothingMaterials = clothingMaterials,
                        GearMaterials = gearMaterials,
                        CacheTime = DateTime.UtcNow
                    };

                    LoneLogging.WriteLine($"[Player Chams] Cached materials for {player.Name} - Clothing: {clothingMaterials.Count}, Gear: {gearMaterials.Count}");
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[Player Chams] Failed to cache materials for {player.Name}: {ex.Message}");
            }
        }

        private static void CacheClothingMaterials(Player player, Dictionary<string, Dictionary<int, int>> clothingMaterials)
        {
            try
            {
                var pRendererContainersArray = Memory.ReadPtr(player.Body + Offsets.PlayerBody._bodyRenderers);
                using var rendererContainersArray = MemArray<Types.BodyRendererContainer>.Get(pRendererContainersArray);

                for (var containerIndex = 0; containerIndex < rendererContainersArray.Count; containerIndex++)
                {
                    var rendererContainer = rendererContainersArray[containerIndex];
                    using var renderersArray = MemArray<ulong>.Get(rendererContainer.Renderers);

                    for (var rendererIndex = 0; rendererIndex < renderersArray.Count; rendererIndex++)
                    {
                        var skinnedMeshRenderer = renderersArray[rendererIndex];
                        var renderer = Memory.ReadPtr(skinnedMeshRenderer + UnityOffsets.SkinnedMeshRenderer.Renderer);

                        var materials = CacheRendererMaterials(renderer);
                        if (materials.Count > 0)
                        {
                            var key = $"clothing_{containerIndex}_{rendererIndex}";
                            clothingMaterials[key] = materials;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[Player Chams] Failed to cache clothing materials: {ex.Message}");
            }
        }

        private static void CacheGearMaterials(Player player, Dictionary<string, Dictionary<int, int>> gearMaterials)
        {
            try
            {
                var slotViews = Memory.ReadValue<ulong>(player.Body + Offsets.PlayerBody.SlotViews);
                if (!Utils.IsValidVirtualAddress(slotViews))
                    return;

                var pSlotViewsDict = Memory.ReadValue<ulong>(slotViews + Offsets.SlotViewsContainer.Dict);
                if (!Utils.IsValidVirtualAddress(pSlotViewsDict))
                    return;

                using var slotViewsDict = MemDictionary<ulong, ulong>.Get(pSlotViewsDict);

                var slotIndex = 0;
                foreach (var slot in slotViewsDict)
                {
                    if (!Utils.IsValidVirtualAddress(slot.Value))
                        continue;

                    var pDressesArray = Memory.ReadValue<ulong>(slot.Value + Offsets.PlayerBodySubclass.Dresses);
                    if (!Utils.IsValidVirtualAddress(pDressesArray))
                        continue;

                    using var dressesArray = MemArray<ulong>.Get(pDressesArray);

                    for (var dressIndex = 0; dressIndex < dressesArray.Count; dressIndex++)
                    {
                        var dress = dressesArray[dressIndex];
                        if (!Utils.IsValidVirtualAddress(dress))
                            continue;

                        var pRenderersArray = Memory.ReadValue<ulong>(dress + Offsets.Dress.Renderers);
                        if (!Utils.IsValidVirtualAddress(pRenderersArray))
                            continue;

                        using var renderersArray = MemArray<ulong>.Get(pRenderersArray);

                        for (var rendererIndex = 0; rendererIndex < renderersArray.Count; rendererIndex++)
                        {
                            var renderer = renderersArray[rendererIndex];
                            if (!Utils.IsValidVirtualAddress(renderer))
                                continue;

                            var rendererNative = Memory.ReadValue<ulong>(renderer + 0x10);
                            if (!Utils.IsValidVirtualAddress(rendererNative))
                                continue;

                            var materials = CacheRendererMaterials(rendererNative);
                            if (materials.Count > 0)
                            {
                                var key = $"gear_{slotIndex}_{dressIndex}_{rendererIndex}";
                                gearMaterials[key] = materials;
                            }
                        }
                    }
                    slotIndex++;
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[Player Chams] Failed to cache gear materials: {ex.Message}");
            }
        }

        private static Dictionary<int, int> CacheRendererMaterials(ulong renderer)
        {
            var materials = new Dictionary<int, int>();

            try
            {
                int materialsCount = Memory.ReadValueEnsure<int>(renderer + UnityOffsets.Renderer.Count);
                if (materialsCount <= 0 || materialsCount > 30)
                    return materials;

                var materialsArrayPtr = Memory.ReadValueEnsure<ulong>(renderer + UnityOffsets.Renderer.Materials);
                if (!Utils.IsValidVirtualAddress(materialsArrayPtr))
                    return materials;

                for (int i = 0; i < materialsCount; i++)
                {
                    var materialId = Memory.ReadValue<int>(materialsArrayPtr + (ulong)(i * 4));
                    materials[i] = materialId;
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[Player Chams] Failed to cache renderer materials: {ex.Message}");
            }

            return materials;
        }

        private static void LoadCache()
        {
            try
            {
                var cache = Config.LowLevelCache.PlayerChamsCache;
                _cachedMaterials.Clear();

                foreach (var kvp in cache)
                    _cachedMaterials[kvp.Key] = kvp.Value;

                LoneLogging.WriteLine($"[Player Chams] Loaded {cache.Count} cached player materials");
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[Player Chams] Failed to load cache: {ex.Message}");
            }
        }

        private static void SaveCache()
        {
            try
            {
                var cache = Config.LowLevelCache.PlayerChamsCache;
                cache.Clear();

                foreach (var kvp in _cachedMaterials)
                    cache[kvp.Key] = kvp.Value;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Config.LowLevelCache.SaveAsync();
                    }
                    catch (Exception ex)
                    {
                        LoneLogging.WriteLine($"[Player Chams] Failed to save cache: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[Player Chams] Failed to prepare cache for saving: {ex.Message}");
            }
        }

        #endregion

        #region Utilities

        private static ChamsEntityType GetEntityType(Player player)
        {
            return player.Type switch
            {
                Player.PlayerType.USEC or Player.PlayerType.BEAR or Player.PlayerType.SpecialPlayer or Player.PlayerType.Streamer => ChamsEntityType.PMC,
                Player.PlayerType.Teammate => ChamsEntityType.Teammate,
                Player.PlayerType.AI => ChamsEntityType.AI,
                _ => ChamsEntityType.AimbotTarget,
            };
        }

        private static ChamsEntityType? GetEntityTypeFromBase(ulong playerBase, LocalGameWorld game)
        {
            var player = game.Players.FirstOrDefault(p => p.Base == playerBase);
            return player != null ? GetEntityType(player) : null;
        }

        public static string GetDiagnosticInfo()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[Player Chams Diagnostic Info]");
            sb.AppendLine($"Active Player States: {_playerStates.Count(s => s.Value.IsActive)}");
            sb.AppendLine($"Cached Player Materials: {_cachedMaterials.Count}");
            sb.AppendLine($"Player States Tracked: {_playerStates.Count}");

            var playerEntityTypes = new[] {
                ChamsEntityType.PMC, ChamsEntityType.Teammate,
                ChamsEntityType.AI, ChamsEntityType.AimbotTarget
            };

            sb.AppendLine("\nMaterial Availability:");
            foreach (var entityType in playerEntityTypes)
                sb.AppendLine($"  {ChamsManager.GetEntityTypeStatus(entityType)}");

            if (_cachedMaterials.Any())
            {
                sb.AppendLine("\nCached Players (Original Materials):");
                foreach (var kvp in _cachedMaterials.Take(10))
                {
                    var cached = kvp.Value;
                    var totalMaterials = cached.ClothingMaterials.Values.Sum(d => d.Count) +
                                       cached.GearMaterials.Values.Sum(d => d.Count);
                    sb.AppendLine($"  {cached.PlayerName}: {totalMaterials} materials (cached {cached.CacheTime:HH:mm:ss})");
                }
                if (_cachedMaterials.Count > 10)
                    sb.AppendLine($"  ... and {_cachedMaterials.Count - 10} more");
            }

            return sb.ToString();
        }

        #endregion

        #region State Class

        private class PlayerChamsState
        {
            public ChamsMode ClothingMode { get; set; } = ChamsMode.Basic;
            public ChamsMode GearMode { get; set; } = ChamsMode.Basic;
            public int ClothingMaterialId { get; set; } = -1;
            public int GearMaterialId { get; set; } = -1;
            public DateTime LastAppliedTime { get; set; }
            public bool IsActive { get; set; }
            public bool IsAimbotTarget { get; set; }
            public bool HasDeathMaterialApplied { get; set; }
        }

        #endregion
    }
}