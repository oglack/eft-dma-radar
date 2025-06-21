﻿using arena_dma_radar.Arena.ArenaPlayer;
using arena_dma_radar.Arena.Features;
using eft_dma_shared.Common.DMA.ScatterAPI;
using eft_dma_shared.Common.Features;
using eft_dma_shared.Common.Misc;
using eft_dma_shared.Common.Players;
using eft_dma_shared.Common.Unity;
using eft_dma_shared.Common.Unity.Collections;

namespace arena_dma_radar.Arena.Features.MemoryWrites
{
    public sealed class FastWeaponOps : MemWriteFeature<FastWeaponOps>
    {
        private bool _lastEnabledState;
        private ulong _lastHandsController;
        private ulong _cachedAnimator;

        private const float FAST_SPEED = 4f;
        private const float NORMAL_SPEED = 1f;
        private const float FAST_AIMING_SPEED = 9999f;
        private const float SPEED_TOLERANCE = 0.1f;

        private static readonly HashSet<string> SupportedControllers = new(StringComparer.OrdinalIgnoreCase)
        {
            "FirearmController",
            "KnifeController",
            "GrenadeHandsController"
        };

        public override bool Enabled
        {
            get => MemWrites.Config.FastWeaponOps;
            set => MemWrites.Config.FastWeaponOps = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(100);

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                if (Memory.LocalPlayer is not LocalPlayer localPlayer)
                    return;

                var handsController = ILocalPlayer.HandsController;
                if (!handsController.IsValidVirtualAddress())
                    return;

                var controllerClassName = ObjectClass.ReadName(handsController);
                var handsControllerChanged = handsController != _lastHandsController;
                var stateChanged = Enabled != _lastEnabledState;

                if (handsControllerChanged)
                {
                    _cachedAnimator = default;
                    _lastHandsController = handsController;
                    LoneLogging.WriteLine($"[FastWeaponOps] Hands controller changed to {controllerClassName}");
                }

                if (!IsSupportedController(controllerClassName))
                    return;

                if (stateChanged || handsControllerChanged)
                {
                    var animator = GetAnimator(localPlayer);
                    if (!animator.IsValidVirtualAddress())
                        return;

                    var currentSpeed = Memory.ReadValue<float>(animator + UnityOffsets.UnityAnimator.Speed, false);
                    ValidateSpeed(currentSpeed);

                    ApplyWeaponOpSettings(writes, localPlayer, animator, currentSpeed, Enabled, controllerClassName);

                    writes.Callbacks += () =>
                    {
                        _lastEnabledState = Enabled;
                    };
                }
                else if (Enabled)
                {
                    writes.AddValueEntry(localPlayer.PWA + Offsets.ProceduralWeaponAnimation._aimingSpeed, FAST_AIMING_SPEED);
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[FastWeaponOps]: {ex}");
                _cachedAnimator = default;
            }
        }

        private ulong GetAnimator(LocalPlayer localPlayer)
        {
            if (_cachedAnimator.IsValidVirtualAddress())
                return _cachedAnimator;

            var pAnimators = Memory.ReadPtr(localPlayer + Offsets.Player._animators);
            if (!pAnimators.IsValidVirtualAddress())
                return 0x0;

            using var animators = MemArray<ulong>.Get(pAnimators);
            if (animators == null || animators.Count <= 1)
                return 0x0;

            var animator = Memory.ReadPtrChain(animators[1], new uint[] {
                Offsets.BodyAnimator.UnityAnimator,
                ObjectClass.MonoBehaviourOffset
            });

            if (!animator.IsValidVirtualAddress())
                return 0x0;

            _cachedAnimator = animator;
            return animator;
        }

        private static bool IsSupportedController(string className)
        {
            return SupportedControllers.Any(controller => className.Contains(controller, StringComparison.OrdinalIgnoreCase));
        }

        private static void ApplyWeaponOpSettings(ScatterWriteHandle writes, LocalPlayer localPlayer, ulong animator,
            float currentSpeed, bool enabled, string controllerClassName)
        {
            var targetSpeed = enabled ? FAST_SPEED : NORMAL_SPEED;
            var needsSpeedChange = Math.Abs(currentSpeed - targetSpeed) > SPEED_TOLERANCE;

            if (enabled)
            {
                writes.AddValueEntry(localPlayer.PWA + Offsets.ProceduralWeaponAnimation._aimingSpeed, FAST_AIMING_SPEED);

                if (needsSpeedChange)
                {
                    writes.AddValueEntry(animator + UnityOffsets.UnityAnimator.Speed, FAST_SPEED);
                    writes.Callbacks += () =>
                        LoneLogging.WriteLine($"[FastWeaponOps] Enabled for {controllerClassName} (Speed: {currentSpeed:F1} -> {FAST_SPEED:F1})");
                }
            }
            else
            {
                if (needsSpeedChange)
                    writes.AddValueEntry(animator + UnityOffsets.UnityAnimator.Speed, NORMAL_SPEED);

                writes.AddValueEntry(localPlayer.PWA + Offsets.ProceduralWeaponAnimation._aimingSpeed, NORMAL_SPEED);

                writes.Callbacks += () =>
                    LoneLogging.WriteLine($"[FastWeaponOps] Disabled for {controllerClassName} (Speed: {currentSpeed:F1} -> {NORMAL_SPEED:F1})");
            }
        }

        private static void ValidateSpeed(float speed)
        {
            if (!float.IsNormal(speed) || speed < NORMAL_SPEED - 0.2f || speed > FAST_SPEED + 0.2f)
                throw new ArgumentOutOfRangeException(nameof(speed),
                    $"Invalid animator speed: {speed} (valid range: {NORMAL_SPEED - 0.2f:F1} - {FAST_SPEED + 0.2f:F1})");
        }

        public override void OnRaidStart()
        {
            _lastEnabledState = default;
            _lastHandsController = default;
            _cachedAnimator = default;
        }
    }
}