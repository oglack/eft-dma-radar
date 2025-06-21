﻿using eft_dma_shared.Common.Misc;
using arena_dma_radar.Arena.Features;
using arena_dma_radar.Arena.GameWorld;
using eft_dma_shared.Common.DMA.ScatterAPI;
using eft_dma_shared.Common.Features;
using eft_dma_shared.Common.Unity;

namespace arena_dma_radar.Arena.Features.MemoryWrites
{
    public sealed class DisableInventoryBlur : MemWriteFeature<DisableInventoryBlur>
    {
        private bool _lastEnabledState;
        private ulong _cachedBlurEffect;

        private const int BLUR_COUNT_DISABLED = 0;
        private const int BLUR_COUNT_ENABLED = 5;
        private const int UPSAMPLE_TEX_DIMENSION_DISABLED = (int)Enums.InventoryBlurDimensions._2048;
        private const int UPSAMPLE_TEX_DIMENSION_ENABLED = (int)Enums.InventoryBlurDimensions._256;

        public override bool Enabled
        {
            get => MemWrites.Config.DisableInventoryBlur;
            set => MemWrites.Config.DisableInventoryBlur = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromSeconds(1);

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                if (Memory.Game is not LocalGameWorld game)
                    return;

                if (Enabled != _lastEnabledState)
                {
                    var blurEffect = GetBlurEffect(game);
                    if (!blurEffect.IsValidVirtualAddress())
                        return;

                    ApplyBlurSettings(writes, blurEffect, Enabled);

                    writes.Callbacks += () =>
                    {
                        _lastEnabledState = Enabled;
                        LoneLogging.WriteLine($"[DisableInventoryBlur] {(Enabled ? "Enabled" : "Disabled")}");
                    };
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[DisableInventoryBlur]: {ex}");
                _cachedBlurEffect = default;
            }
        }

        private ulong GetBlurEffect(LocalGameWorld game)
        {
            if (_cachedBlurEffect.IsValidVirtualAddress())
                return _cachedBlurEffect;

            var fps = game.CameraManager?.FPSCamera ?? 0x0;
            if (!fps.IsValidVirtualAddress())
                return 0x0;

            var blurEffect = MonoBehaviour.GetComponent(fps, "InventoryBlur");
            if (!blurEffect.IsValidVirtualAddress())
                return 0x0;

            _cachedBlurEffect = blurEffect;
            return blurEffect;
        }

        private static void ApplyBlurSettings(ScatterWriteHandle writes, ulong blurEffect, bool disableBlur)
        {
            var (blurCount, upsampleTexDimension) = disableBlur
                ? (BLUR_COUNT_DISABLED, UPSAMPLE_TEX_DIMENSION_DISABLED)
                : (BLUR_COUNT_ENABLED, UPSAMPLE_TEX_DIMENSION_ENABLED);

            writes.AddValueEntry(blurEffect + Offsets.InventoryBlur._blurCount, blurCount);
            writes.AddValueEntry(blurEffect + Offsets.InventoryBlur._upsampleTexDimension, upsampleTexDimension);
        }

        public override void OnRaidStart()
        {
            _lastEnabledState = default;
            _cachedBlurEffect = default;
        }
    }
}