using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.Tarkov.Features;
using eft_dma_radar.Tarkov.GameWorld;
using eft_dma_shared.Common.Misc;
using eft_dma_shared.Common.DMA.ScatterAPI;
using eft_dma_shared.Common.Features;
using eft_dma_shared.Common.Unity;
using eft_dma_shared.Common.Unity.Collections;

namespace eft_dma_radar.Tarkov.Features.MemoryWrites
{
    public sealed class NoFall : MemWriteFeature<NoFall>
    {
        private bool _lastEnabledState;
        private bool stickDisabled;

        public override bool Enabled
        {
            get => MemWrites.Config.NoFall;
            set => MemWrites.Config.NoFall = value;
        }
        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                if (Memory.LocalPlayer is not LocalPlayer localPlayer)
                    return;

                if (Enabled != _lastEnabledState)
                {
                    ulong pMovementStateDict = Memory.ReadPtr(localPlayer.MovementContext + Offsets.MovementContext._states, false);
                    using var movementStateDict = MemDictionary<ulong, ulong>.Get(pMovementStateDict, false);
                    if (!movementStateDict.Any())
                        throw new Exception(nameof(movementStateDict));

                    foreach (var movementState in movementStateDict)
                    {
                        writes.AddValueEntry(movementState.Value + Offsets.MovementState.StickToGround, false);
                    }
                    if (stickDisabled)
                    {
                        stickDisabled = false;
                    }
                }
                else
                {
                    if (stickDisabled) return;
                    ulong pMovementStateDict = Memory.ReadPtr(localPlayer.MovementContext + Offsets.MovementContext._states, false);
                    using var movementStateDict = MemDictionary<ulong, ulong>.Get(pMovementStateDict, false);
                    if (!movementStateDict.Any())
                        throw new Exception(nameof(movementStateDict));

                    foreach (var movementState in movementStateDict)
                    {
                        writes.AddValueEntry(movementState.Value + Offsets.MovementState.StickToGround, true);
                    }
                    stickDisabled = true;
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[NoFall]: {ex}");
            }
        }
    }
}