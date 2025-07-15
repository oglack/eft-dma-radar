using eft_dma_shared.Common.Misc;
using eft_dma_shared.Common.Misc.Config;
using eft_dma_shared.Common.Unity.LowLevel.Chams;
using eft_dma_shared.Common.Unity.LowLevel.Hooks;
using eft_dma_shared.Common.Unity.LowLevel.Types;
using eft_dma_shared.Misc;
using SkiaSharp;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace eft_dma_shared.Common.Unity.LowLevel.Chams.Arena
{
    public static class ChamsManager
    {
        public static event Action MaterialsUpdated;

        private static readonly Stopwatch _rateLimit = new();

        private static readonly FrozenDictionary<(ChamsMode, ChamsEntityType), string> BundleMapping =
            new Dictionary<(ChamsMode, ChamsEntityType), string>
            {
                { (ChamsMode.VisCheckGlow, ChamsEntityType.AI), "visibilitycheck.bundle" },
                { (ChamsMode.VisCheckFlat, ChamsEntityType.AI), "vischeckflat.bundle" },
                { (ChamsMode.WireFrame, ChamsEntityType.AI), "wireframepmc.bundle" },
                { (ChamsMode.VisCheckGlow, ChamsEntityType.PMC), "visibilitycheck.bundle" },
                { (ChamsMode.VisCheckFlat, ChamsEntityType.PMC), "vischeckflat.bundle" },
                { (ChamsMode.WireFrame, ChamsEntityType.PMC), "wireframepmc.bundle" },
                { (ChamsMode.VisCheckGlow, ChamsEntityType.Teammate), "visibilitycheck.bundle" },
                { (ChamsMode.VisCheckFlat, ChamsEntityType.Teammate), "vischeckflat.bundle" },
                { (ChamsMode.WireFrame, ChamsEntityType.Teammate), "wireframepmc.bundle" },
                { (ChamsMode.VisCheckGlow, ChamsEntityType.AimbotTarget), "visibilitycheck.bundle" },
                { (ChamsMode.VisCheckFlat, ChamsEntityType.AimbotTarget), "vischeckflat.bundle" },
                { (ChamsMode.WireFrame, ChamsEntityType.AimbotTarget), "wireframepmc.bundle" },
                { (ChamsMode.VisCheckGlow, ChamsEntityType.Grenade), "visibilitycheck.bundle" },
                { (ChamsMode.VisCheckFlat, ChamsEntityType.Grenade), "vischeckflat.bundle" },
                { (ChamsMode.WireFrame, ChamsEntityType.Grenade), "wireframepmc.bundle" },
            }.ToFrozenDictionary();

        private static readonly MonoString VisibleColorStr = new("_ColorVisible");
        private static readonly MonoString InvisibleColorStr = new("_ColorInvisible");

        private const string DefaultVisibleColor = "#00FF00";
        private const string DefaultInvisibleColor = "#FF0000";

        private const int NotificationBatchSize = 5;
        private const int MaterialCreationTimeoutSeconds = 12;
        private const int RetryDelayMs = 250;
        private const int MaterialInstancePollDelayMs = 10;

        private static LowLevelCache Cache => SharedProgram.Config.LowLevelCache;

        private static readonly ConcurrentDictionary<(ChamsMode, ChamsEntityType), ChamsMaterial> _materials = new();
        public static IReadOnlyDictionary<(ChamsMode, ChamsEntityType), ChamsMaterial> Materials => _materials;
        private static readonly ConcurrentDictionary<(ChamsMode, ChamsEntityType), DateTime> _failedMaterials = new();

        public static int ExpectedMaterialCount => BundleMapping.Count;

        #region Public API

        public static bool Initialize()
        {
            if (Materials.Count > 0)
                return true;

            if (!_rateLimit.IsRunning)
            {
                _rateLimit.Start();
                return false;
            }

            if (_rateLimit.Elapsed < TimeSpan.FromSeconds(10))
                return false;

            try
            {
                if (!NativeHook.Initialized)
                    return false;

                if (TryInitializeFromCache())
                    return true;

                return InitializeFromBundles();
            }
            catch (Exception ex)
            {
                _materials.Clear();
                Cache.ChamsMaterialCache.Clear();
                LoneLogging.WriteLine("[CHAMS MANAGER] Initialize() -> ERROR: " + ex);
                return false;
            }
            finally
            {
                _rateLimit.Restart();
            }
        }

        public static bool ForceInitialize()
        {
            try
            {
                if (!NativeHook.Initialized)
                    return false;

                if (TryInitializeFromCache())
                    return true;

                return InitializeFromBundles();
            }
            catch (Exception ex)
            {
                _materials.Clear();
                Cache.ChamsMaterialCache.Clear();
                LoneLogging.WriteLine("[CHAMS MANAGER] ForceInitialize() -> ERROR: " + ex);
                return false;
            }
            finally
            {
                _rateLimit.Restart();
            }
        }

        public static int GetMaterialIDForPlayer(ChamsMode mode, ChamsEntityType playerType)
        {
            if (!IsPlayerEntityType(playerType))
            {
                LoneLogging.WriteLine($"[Chams] Warning: {playerType} is not a valid player entity type");
                return -1;
            }

            return GetStandardMaterialId(mode, playerType);
        }

        public static int GetMaterialIDForLoot(ChamsMode mode, ChamsEntityType lootType)
        {
            if (!IsLootEntityType(lootType))
            {
                LoneLogging.WriteLine($"[Chams] Warning: {lootType} is not a valid player entity type");
                return -1;
            }

            return GetStandardMaterialId(mode, lootType);
        }

        public static bool AreMaterialsReadyForEntityType(ChamsEntityType entityType)
        {
            var requiredModes = new[] { ChamsMode.VisCheckFlat, ChamsMode.VisCheckGlow, ChamsMode.WireFrame };

            return requiredModes.All(mode =>
                Materials.TryGetValue((mode, entityType), out var material) &&
                material.InstanceID != 0);
        }

        public static List<ChamsMode> GetAvailableModesForEntityType(ChamsEntityType entityType)
        {
            var availableModes = new List<ChamsMode>();
            availableModes.Add(ChamsMode.Basic);
            availableModes.Add(ChamsMode.Visible);

            var advancedModes = new[] { ChamsMode.VisCheckFlat, ChamsMode.VisCheckGlow, ChamsMode.WireFrame };

            foreach (var mode in advancedModes)
            {
                if (Materials.TryGetValue((mode, entityType), out var material) && material.InstanceID != 0)
                {
                    availableModes.Add(mode);
                }
            }

            return availableModes;
        }

        public static string GetEntityTypeStatus(ChamsEntityType entityType)
        {
            var totalModes = BundleMapping.Keys.Where(k => k.Item2 == entityType).Count();
            var loadedModes = Materials.Where(m => m.Key.Item2 == entityType && m.Value.InstanceID != 0).Count();

            return $"{entityType}: {loadedModes}/{totalModes} materials loaded";
        }

        public static ChamsMaterialStatus GetDetailedStatus()
        {
            var expectedCount = BundleMapping.Count;
            var currentCount = _materials.Count;
            var workingCount = _materials.Count(kvp => kvp.Value.InstanceID != 0);
            var failedCount = _failedMaterials.Count;

            return new ChamsMaterialStatus
            {
                ExpectedCount = expectedCount,
                LoadedCount = currentCount,
                WorkingCount = workingCount,
                FailedCount = failedCount,
                MissingCombos = BundleMapping.Keys.Where(combo => !_materials.ContainsKey(combo) ||
                                                                 _materials[combo].InstanceID == 0).ToList(),
                FailedCombos = _failedMaterials.Keys.ToList()
            };
        }

        public static bool IsPlayerEntityType(ChamsEntityType entityType)
        {
            return entityType switch
            {
                ChamsEntityType.PMC or
                ChamsEntityType.Teammate or
                ChamsEntityType.AI or
                ChamsEntityType.AimbotTarget => true,
                _ => false
            };
        }

        public static bool IsLootEntityType(ChamsEntityType entityType)
        {
            return entityType switch
            {
                ChamsEntityType.Grenade => true,
                _ => false
            };
        }

        public static bool RefreshFailedMaterials()
        {
            try
            {
                if (!NativeHook.Initialized)
                    return false;

                var expectedCombos = BundleMapping.Keys.ToList();
                var missingCombos = expectedCombos.Where(combo => !_materials.ContainsKey(combo) ||
                                                                 _materials[combo].InstanceID == 0).ToList();

                if (missingCombos.Count == 0)
                {
                    LoneLogging.WriteLine("[CHAMS REFRESH] No missing materials found");
                    return true;
                }

                LoneLogging.WriteLine($"[CHAMS REFRESH] Found {missingCombos.Count} missing materials, attempting targeted refresh...");

                var chamsConfig = SharedProgram.Config.ChamsConfig;
                var unityObjects = GetUnityObjects();
                if (!unityObjects.HasValue)
                    return false;

                var unityObjectsValue = unityObjects.Value;
                using var visibleColorMem = VisibleColorStr.ToRemoteBytes();
                using var invisibleColorMem = InvisibleColorStr.ToRemoteBytes();
                using var chamsColorMem = new RemoteBytes(SizeChecker<UnityColor>.Size);

                var successCount = 0;
                var retryDelayMs = 300;

                foreach (var (mode, playerType) in missingCombos)
                {
                    try
                    {
                        NotificationsShared.Info($"[CHAMS REFRESH] Retrying {mode} - {playerType}");

                        if (TryCreateMaterial(mode, playerType, unityObjectsValue, chamsConfig,
                            visibleColorMem, invisibleColorMem, chamsColorMem))
                        {
                            successCount++;
                            LoneLogging.WriteLine($"[CHAMS REFRESH] Successfully recovered {mode} - {playerType}");
                            _failedMaterials.TryRemove((mode, playerType), out _);
                        }
                        else
                        {
                            LoneLogging.WriteLine($"[CHAMS REFRESH] Still failed: {mode} - {playerType}");
                            _failedMaterials[(mode, playerType)] = DateTime.Now;
                        }

                        Thread.Sleep(retryDelayMs);
                    }
                    catch (Exception ex)
                    {
                        LoneLogging.WriteLine($"[CHAMS REFRESH] Error refreshing {mode}-{playerType}: {ex.Message}");
                        _failedMaterials[(mode, playerType)] = DateTime.Now;
                    }
                }

                if (successCount > 0)
                {
                    CacheMaterialIds();
                    LoneLogging.WriteLine($"[CHAMS REFRESH] Successfully recovered {successCount}/{missingCombos.Count} materials");
                    NotifyMaterialsUpdated();
                }

                return successCount == missingCombos.Count;
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[CHAMS REFRESH] RefreshFailedMaterials() -> ERROR: {ex}");
                return false;
            }
        }

        public static bool SmartRefresh()
        {
            try
            {
                if (RefreshFailedMaterials())
                {
                    NotificationsShared.Success("[CHAMS] All missing materials recovered!");
                    NotifyMaterialsUpdated();
                    return true;
                }

                var currentCount = _materials.Count;
                var expectedCount = BundleMapping.Count;

                if (currentCount < expectedCount * 0.5)
                {
                    LoneLogging.WriteLine("[CHAMS REFRESH] Less than 50% materials loaded, performing full refresh...");
                    NotificationsShared.Info("[CHAMS] Performing full material refresh...");

                    var successfulMaterials = _materials.Where(kvp => kvp.Value.InstanceID != 0)
                                                       .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

                    _materials.Clear();

                    foreach (var kvp in successfulMaterials)
                    {
                        _materials[kvp.Key] = kvp.Value;
                    }

                    var result = ForceInitialize();
                    if (result)
                        NotifyMaterialsUpdated();
                    return result;
                }
                else
                {
                    NotificationsShared.Warning($"[CHAMS] Partial recovery: {currentCount}/{expectedCount} materials loaded");
                    if (currentCount > 0)
                        NotifyMaterialsUpdated();
                    return currentCount > 0;
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[CHAMS REFRESH] SmartRefresh() -> ERROR: {ex}");
                return false;
            }
        }

        public static void Reset()
        {
            _materials.Clear();
            _failedMaterials.Clear();
            _rateLimit.Reset();
        }

        #endregion

        #region Private Implementation

        private static void NotifyMaterialsUpdated()
        {
            try
            {
                MaterialsUpdated?.Invoke();
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[CHAMS] Error notifying materials updated: {ex.Message}");
            }
        }

        private static int GetStandardMaterialId(ChamsMode mode, ChamsEntityType entityType)
        {
            if (Materials.TryGetValue((mode, entityType), out var material) && material.InstanceID != 0)
                return material.InstanceID;

            return -1;
        }

        private static bool InitializeFromBundles()
        {
            Cache.ChamsMaterialCache.Clear();
            var chamsConfig = SharedProgram.Config.ChamsConfig;

            var unityObjects = GetUnityObjects();
            if (!unityObjects.HasValue)
                return false;

            var unityObjectsValue = unityObjects.Value;
            using var visibleColorMem = VisibleColorStr.ToRemoteBytes();
            using var invisibleColorMem = InvisibleColorStr.ToRemoteBytes();
            using var chamsColorMem = new RemoteBytes(SizeChecker<UnityColor>.Size);

            var allCombos = BundleMapping.Keys.ToList();
            var failedCombos = ProcessMaterialBundles(allCombos, unityObjectsValue, chamsConfig,
                visibleColorMem, invisibleColorMem, chamsColorMem);

            if (failedCombos.Count > 0)
            {
                var successCount = allCombos.Count - failedCombos.Count;

                foreach (var (mode, playerType) in failedCombos)
                {
                    _failedMaterials[(mode, playerType)] = DateTime.Now;
                }

                LoneLogging.WriteLine($"[CHAMS] Initial load: {successCount}/{allCombos.Count} materials loaded successfully");
                NotificationsShared.Warning($"[CHAMS] Initial load: {successCount}/{allCombos.Count} materials loaded. {failedCombos.Count} materials failed.");
                NotificationsShared.Info("[CHAMS] Use 'Refresh Materials' button to retry failed materials.");

                foreach (var (mode, type) in failedCombos.Take(5))
                {
                    LoneLogging.WriteLine($"[CHAMS] Failed to load: {mode} - {type}");
                }

                if (failedCombos.Count > 5)
                    LoneLogging.WriteLine($"[CHAMS] ... and {failedCombos.Count - 5} more failed materials");
            }
            else
            {
                NotificationsShared.Success("[CHAMS] All materials successfully loaded!");
                LoneLogging.WriteLine("[CHAMS] All materials loaded successfully on first attempt");
            }

            if (_materials.Count > 0)
            {
                CacheMaterialIds();
                LoneLogging.WriteLine("[CHAMS] Materials created successfully - notifying managers for color application");
                NotifyMaterialsUpdated();
            }

            LoneLogging.WriteLine($"[CHAMS MANAGER] Initialize() -> Completed with {_materials.Count}/{allCombos.Count} materials");

            return _materials.Count > 0;
        }

        private static UnityObjects? GetUnityObjects()
        {
            var monoDomain = (ulong)MonoLib.MonoRootDomain.Get();
            if (!monoDomain.IsValidVirtualAddress())
                throw new Exception("Failed to get mono domain!");

            MonoLib.MonoClass.Find("UnityEngine.CoreModule", "UnityEngine.Shader", out ulong shaderClassAddr);
            shaderClassAddr.ThrowIfInvalidVirtualAddress();
            var shaderType = shaderClassAddr + 0xB8;

            var shaderTypeObject = NativeMethods.GetTypeObject(monoDomain, shaderType);
            if (!shaderTypeObject.IsValidVirtualAddress())
                throw new Exception("Failed to get UnityEngine.Shader Type Object!");

            MonoLib.MonoClass.Find("UnityEngine.CoreModule", "UnityEngine.Material", out ulong materialClass);
            if (!materialClass.IsValidVirtualAddress())
                throw new Exception("Failed to get UnityEngine.Material class!");

            return new UnityObjects(monoDomain, materialClass, shaderTypeObject);
        }

        private static List<(ChamsMode, ChamsEntityType)> ProcessMaterialBundles(
            List<(ChamsMode, ChamsEntityType)> combos,
            UnityObjects unityObjects,
            ChamsConfig chamsConfig,
            ulong visibleColorMem,
            ulong invisibleColorMem,
            RemoteBytes chamsColorMem)
        {
            var failedCombos = new List<(ChamsMode, ChamsEntityType)>();

            NotificationsShared.Info("[CHAMS] Loading Materials..");
            NotificationsShared.Info("[CHAMS] Please Stay In MainMenu...");

            for (int i = 0; i < combos.Count; i++)
            {
                var (mode, playerType) = combos[i];

                if ((i + 1) % NotificationBatchSize == 0)
                {
                    string status = $"Loading {i + 1}/{combos.Count}: {mode} - {playerType}";
                    NotificationsShared.Info("[CHAMS LOAD] Please Wait " + status);
                }

                if (!TryCreateMaterial(mode, playerType, unityObjects, chamsConfig,
                    visibleColorMem, invisibleColorMem, chamsColorMem))
                {
                    failedCombos.Add((mode, playerType));
                }
            }

            return failedCombos;
        }

        private static bool TryCreateMaterial(
            ChamsMode mode,
            ChamsEntityType playerType,
            UnityObjects unityObjects,
            dynamic chamsConfig,
            ulong visibleColorMem,
            ulong invisibleColorMem,
            RemoteBytes chamsColorMem)
        {
            try
            {
                var bundleName = BundleMapping[(mode, playerType)];

                using var stream = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream($"eft_dma_shared.{bundleName}");
                if (stream == null)
                    return false;

                byte[] resource = new byte[stream.Length];
                stream.ReadExactly(resource);

                var assetBundleMonoByteArr = new MonoByteArray(resource);
                using var assetBundleMem = assetBundleMonoByteArr.ToRemoteBytes();

                var shaderName = Path.GetFileNameWithoutExtension(bundleName);
                var shaderMonoStr = new MonoString(shaderName);
                using var shaderMem = shaderMonoStr.ToRemoteBytes();

                if (!AssetFactory.LoadBundle(assetBundleMem, shaderMem, unityObjects.ShaderTypeObject))
                    throw new Exception($"Failed to load bundle: {bundleName}");

                var visibleColorStr = DefaultVisibleColor;
                var invisibleColorStr = DefaultInvisibleColor;

                try
                {
                    var entitySettings = chamsConfig.GetEntitySettings(playerType);
                    var materialColorSettings = entitySettings.MaterialColors?.ContainsKey(mode) == true
                        ? entitySettings.MaterialColors[mode]
                        : null;

                    if (materialColorSettings != null)
                    {
                        visibleColorStr = materialColorSettings.VisibleColor ?? DefaultVisibleColor;
                        invisibleColorStr = materialColorSettings.InvisibleColor ?? DefaultInvisibleColor;
                    }
                    else
                    {
                        visibleColorStr = entitySettings.VisibleColor ?? DefaultVisibleColor;
                        invisibleColorStr = entitySettings.InvisibleColor ?? DefaultInvisibleColor;
                    }
                }
                catch (Exception ex)
                {
                    LoneLogging.WriteLine($"[CHAMS] Error getting colors for {mode}/{playerType}: {ex.Message}");
                }

                var visibleColor = SKColor.Parse(visibleColorStr);
                var invisibleColor = SKColor.Parse(invisibleColorStr);

                var material = CreateChamsMaterial(unityObjects.MonoDomain, unityObjects.MaterialClass,
                    invisibleColorMem, visibleColorMem, true);

                NativeMethods.SetMaterialColor(chamsColorMem, material.Address, material.ColorVisible, new UnityColor(visibleColor));
                NativeMethods.SetMaterialColor(chamsColorMem, material.Address, material.ColorInvisible, new UnityColor(invisibleColor));

                _materials[(mode, playerType)] = material;

                AssetFactory.UnloadLoadedAssetBundle();
                Thread.Sleep(RetryDelayMs);

                return true;
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[CHAMS MANAGER] Failed to create material {mode}-{playerType}: {ex.Message}");
                return false;
            }
        }

        private static bool TryInitializeFromCache()
        {
            try
            {
                var codeCave = NativeHook.CodeCave;
                if (!codeCave.IsValidVirtualAddress())
                    return false;

                var cache = Cache.ChamsMaterialCache;
                if (Cache.CodeCave == codeCave && !cache.IsEmpty)
                {
                    using var visibleColorMem = VisibleColorStr.ToRemoteBytes();
                    using var invisibleColorMem = InvisibleColorStr.ToRemoteBytes();
                    var visibleColorId = AssetFactory.ShaderPropertyToID(visibleColorMem);
                    var invisibleColorId = AssetFactory.ShaderPropertyToID(invisibleColorMem);
                    var expectedCapacity = Math.Max(cache.Count, BundleMapping.Count);
                    var tempMaterials = new Dictionary<(ChamsMode, ChamsEntityType), ChamsMaterial>(expectedCapacity);

                    foreach (var kvp in cache)
                    {
                        var (mode, ptype) = ParseCachedKey(kvp.Key);
                        var cached = kvp.Value;

                        var mat = new ChamsMaterial
                        {
                            Address = cached.Address,
                            InstanceID = cached.InstanceID,
                            ColorVisible = visibleColorId,
                            ColorInvisible = invisibleColorId
                        };

                        tempMaterials[(mode, ptype)] = mat;
                    }

                    foreach (var kvp in tempMaterials)
                    {
                        _materials[kvp.Key] = kvp.Value;
                    }

                    LoneLogging.WriteLine("[CHAMS MANAGER] TryInitializeFromCache() -> OK");
                    LoneLogging.WriteLine($"[CHAMS CACHE] Loaded {_materials.Count} materials from cache");
                    NotificationsShared.Info($"[CHAMS CACHE] Loaded {_materials.Count} materials from cache");
                    return true;
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[CHAMS CACHE] Cache load failed: {ex.Message}");
                _materials.Clear();
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static (ChamsMode, ChamsEntityType) ParseCachedKey(int combinedKey)
        {
            var mode = combinedKey & 0xFF;
            var ptype = (combinedKey >> 8) & 0xFF;
            return ((ChamsMode)mode, (ChamsEntityType)ptype);
        }

        private static void CacheMaterialIds()
        {
            var cache = Cache.ChamsMaterialCache;
            cache.Clear();

            var tempCache = new Dictionary<int, CachedChamsMaterial>(_materials.Count);

            foreach (var kvp in _materials)
            {
                var combinedKey = ((int)kvp.Key.Item2 << 8) | (int)kvp.Key.Item1;
                tempCache[combinedKey] = new CachedChamsMaterial
                {
                    InstanceID = kvp.Value.InstanceID,
                    Address = kvp.Value.Address
                };
            }

            foreach (var kvp in tempCache)
            {
                cache[kvp.Key] = kvp.Value;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await Cache.SaveAsync();
                }
                catch (Exception ex)
                {
                    LoneLogging.WriteLine($"[CHAMS CACHE] Failed to save cache: {ex.Message}");
                }
            });
        }

        private static ChamsMaterial CreateChamsMaterial(ulong monoDomain, ulong materialClass,
            ulong invisibleColorMem, ulong visibleColorMem, bool hasInvisibleColor)
        {
            var materialAddress = AssetFactory.CreateMaterial(monoDomain, materialClass);
            if (materialAddress == 0x0)
                throw new Exception("CreateChamsMaterial() -> Failed to create the material from shader!");

            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < MaterialCreationTimeoutSeconds)
            {
                try
                {
                    ulong materialInstance = Memory.ReadValueEnsure<ulong>(materialAddress + ObjectClass.MonoBehaviourOffset);
                    materialInstance.ThrowIfInvalidVirtualAddress();

                    int instanceID = Memory.ReadValueEnsure<int>(materialInstance + MonoBehaviour.InstanceIDOffset);
                    ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(instanceID, 0);

                    LoneLogging.WriteLine("[CHAMS MANAGER]: Created material instance: " + instanceID);

                    return new()
                    {
                        Address = materialAddress,
                        InstanceID = instanceID,
                        ColorVisible = AssetFactory.ShaderPropertyToID(visibleColorMem),
                        ColorInvisible = hasInvisibleColor ? AssetFactory.ShaderPropertyToID(invisibleColorMem) : int.MinValue
                    };
                }
                catch
                {
                    Thread.Sleep(MaterialInstancePollDelayMs);
                }
            }

            throw new Exception("CreateChamsMaterial() -> Timeout waiting for material instance!");
        }

        #endregion

        #region Types

        private readonly record struct UnityObjects(ulong MonoDomain, ulong MaterialClass, ulong ShaderTypeObject);

        #endregion
    }
}