using eft_dma_shared.Common.Features;
using eft_dma_shared.Common.Misc;
using eft_dma_shared.Common.Misc.Data;
using eft_dma_shared.Common.Unity;
using eft_dma_shared.Common.Unity.Collections;

namespace eft_dma_radar.Tarkov.Features.MemoryWrites
{
    public sealed class UnlockMaps : MemWriteFeature<UnlockMaps>
    {
        /// <summary>
        /// Set maps unlock.
        /// </summary>
        /// <exception cref="Exception"></exception>
        public void Set()
        {
            try
            {
                var gom = GameObjectManager.Get(Memory.UnityBase);
                var applicationGO = gom.GetObjectFromList("Application (Main Client)");
                ArgumentOutOfRangeException.ThrowIfZero(applicationGO, nameof(applicationGO));

                var tarkovApplication = GameObject.GetComponent(applicationGO, "TarkovApplication");
                ArgumentOutOfRangeException.ThrowIfZero(tarkovApplication, nameof(tarkovApplication));
                var clientbackend = Memory.ReadPtrChain(tarkovApplication, new uint[] { Offsets.TarkovApplication.MenuOperation, 0x48 });
                var locationsettings = Memory.ReadPtr(clientbackend + 0x1B0);
                var plocationsdict = Memory.ReadPtr(locationsettings + 0x10);
                using var locationsDict = MemDictionary<ulong, ulong>.Get(plocationsdict);
                if (!locationsDict.Any())
                    throw new Exception(nameof(locationsDict));

                foreach (var location in locationsDict)
                {
                    var mapnameptr = Memory.ReadPtr(location.Value + 0x18);
                    if (GameData.MapNames.ContainsKey(Memory.ReadUnityString(mapnameptr, 64, false)))
                    {
                        Memory.WriteValue<bool>(location.Value + 0xEF, false);
                        Memory.WriteValue<bool>(location.Value + 0x179, false);
                        Memory.WriteValue<bool>(location.Value + 0xEC, true);
                    }
                    else continue;
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"ERROR unlocking maps", ex);
            }
        }
    }
}