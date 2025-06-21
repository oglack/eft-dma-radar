using arena_dma_radar.Arena.ArenaPlayer;
using arena_dma_radar.Arena.Features;
using arena_dma_radar.Arena.GameWorld;
using eft_dma_shared.Common.DMA.ScatterAPI;
using eft_dma_shared.Common.Features;
using eft_dma_shared.Common.Misc;
using eft_dma_shared.Common.Players;
using eft_dma_shared.Common.Unity;

namespace arena_dma_radar.Arena.Features.MemoryWrites
{
    public sealed class ThirdPerson : MemWriteFeature<ThirdPerson>
    {
        private bool _lastEnabledState;
        private ulong _cachedHandsContainer;

        public override bool Enabled
        {
            get => MemWrites.Config.ThirdPerson;
            set => MemWrites.Config.ThirdPerson = value;
        }

        private static readonly Vector3 THIRD_PERSON_ON = new(0.04f, 0.14f, -2.2f);
        private static readonly Vector3 THIRD_PERSON_OFF = new(0.04f, 0.04f, 0.05f);

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(500);

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                if (Memory.LocalPlayer is not LocalPlayer localPlayer)
                    return;

                var handsContainer = GetHandsContainer(localPlayer);
                if (!handsContainer.IsValidVirtualAddress())
                    return;

                if (Enabled != _lastEnabledState)
                {
                    var offset = Enabled ? THIRD_PERSON_ON : THIRD_PERSON_OFF;
                    writes.AddValueEntry(handsContainer + Offsets.HandsContainer.CameraOffset, offset);

                    writes.Callbacks += () =>
                    {
                        _lastEnabledState = Enabled;
                        LoneLogging.WriteLine($"[ThirdPerson] {(Enabled ? "Enabled" : "Disabled")}");
                    };
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[ThirdPerson]: {ex}");
            }
        }

        private ulong GetHandsContainer(LocalPlayer localPlayer)
        {
            if (_cachedHandsContainer.IsValidVirtualAddress())
                return _cachedHandsContainer;

            var PWA = localPlayer.PWA;
            if (!PWA.IsValidVirtualAddress()) 
                return 0x0;

            var handsContainer = Memory.ReadPtr(PWA + Offsets.ProceduralWeaponAnimation.HandsContainer);
            if (!handsContainer.IsValidVirtualAddress()) 
                return 0x0;

            _cachedHandsContainer = handsContainer;
            return handsContainer;
        }

        public override void OnRaidStart()
        {
            _lastEnabledState = default;
            _cachedHandsContainer = default;
        }
    }
}