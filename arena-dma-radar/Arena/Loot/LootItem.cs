using arena_dma_radar.Arena.ArenaPlayer;
using arena_dma_radar.UI.ESP;
using arena_dma_radar.UI.Misc;
using arena_dma_radar.UI.Radar;
using eft_dma_shared.Common.Maps;
using eft_dma_shared.Common.Misc;
using eft_dma_shared.Common.Misc.Data;
using eft_dma_shared.Common.Players;
using eft_dma_shared.Common.Unity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace arena_dma_radar.Arena.Loot
{
    public class LootItem : IMouseoverEntity, IMapEntity, IWorldEntity, IESPEntity
    {
        private static Config Config => Program.Config;
        private readonly TarkovMarketItem _item;

        public static EntityTypeSettings LootSettings => Config.EntityTypeSettings.GetSettings("Loot");
        public static EntityTypeSettingsESP LootESPSettings => ESP.Config.EntityTypeESPSettings.GetSettings("Loot");

        private const float HEIGHT_INDICATOR_THRESHOLD = 1.85f;

        public LootItem(TarkovMarketItem item)
        {
            ArgumentNullException.ThrowIfNull(item, nameof(item));
            _item = item;
        }

        public LootItem(string id, string name)
        {
            ArgumentNullException.ThrowIfNull(id, nameof(id));
            ArgumentNullException.ThrowIfNull(name, nameof(name));
            _item = new TarkovMarketItem
            {
                Name = name,
                ShortName = name,
                FleaPrice = -1,
                TraderPrice = -1,
                BsgId = id,
                Tags = name == "Bomb" ? ["Backpack"] :
                       name == "M18" ? ["Throwable weapon"] :
                       []
            };
        }

        /// <summary>
        /// Item's BSG ID.
        /// </summary>
        public virtual string ID => _item.BsgId;

        /// <summary>
        /// Item's Long Name.
        /// </summary>
        public virtual string Name => _item.Name;

        /// <summary>
        /// Item's Short Name.
        /// </summary>
        public string ShortName => _item.ShortName;

        public ulong InteractiveClass { get; set; }
        static Dictionary<ulong, List<int>> _originalMaterials = new();

        /// <summary>
        /// Number of grid spaces this item takes up.
        /// </summary>
        public int GridCount => _item.Slots == 0 ? 1 : _item.Slots;

        /// <summary>
        /// Custom filter for this item (if set).
        /// </summary>
        public LootFilterEntry CustomFilter => _item.CustomFilter;

        /// <summary>
        /// True if the item is important via the UI.
        /// </summary>
        public bool Important => CustomFilter?.Important ?? false;

        /// <summary>
        /// True if the item is blacklisted via the UI.
        /// </summary>
        public bool Blacklisted => CustomFilter?.Blacklisted ?? false;

        public bool IsMeds
        {
            get
            {
                if (this is LootContainer container)
                    return container.Loot.Any(x => x.IsMeds);

                return _item.IsMed;
            }
        }

        public bool IsFood
        {
            get
            {
                if (this is LootContainer container)
                    return container.Loot.Any(x => x.IsFood);

                return _item.IsFood;
            }
        }

        public bool IsBackpack
        {
            get
            {
                if (this is LootContainer container)
                    return container.Loot.Any(x => x.IsBackpack);

                return _item.IsBackpack;
            }
        }

        public bool IsWeapon => _item.IsWeapon;

        public bool IsThrowableWeapon => _item.IsThrowableWeapon;

        public bool IsWeaponMod => _item.IsWeaponMod;

        public bool IsCurrency => _item.IsCurrency;

        public bool IsRegularLoot
        {
            get
            {
                if (Blacklisted)
                    return false;

                if (this is LootContainer container)
                    return container.Loot.Any(x => x.IsRegularLoot);

                return true;

            }
        }

        public bool IsImportant
        {
            get
            {
                if (Blacklisted)
                    return false;

                if (this is LootContainer container)
                    return container.Loot.Any(x => x.IsImportant);

                return _item.Important;
            }
        }

        /// <summary>
        /// True if this item contains the specified Search Predicate.
        /// </summary>
        /// <param name="predicate"></param>
        /// <returns>True if search matches, otherwise False.</returns>
        public bool ContainsSearchPredicate(Predicate<LootItem> predicate)
        {
            if (this is LootContainer container)
                return container.Loot.Any(x => x.ContainsSearchPredicate(predicate));

            return predicate(this);
        }

        public virtual void Draw(SKCanvas canvas, LoneMapParams mapParams, ILocalPlayer localPlayer)
        {
            EntityTypeSettings entitySettings = LootSettings;

            var dist = Vector3.Distance(localPlayer.Position, Position);
            if (dist > entitySettings.RenderDistance)
                return;

            var label = GetEntityUILabel(entitySettings);
            var paints = GetPaints();
            var heightDiff = Position.Y - localPlayer.Position.Y;
            var point = Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams);
            MouseoverPosition = new Vector2(point.X, point.Y);
            SKPaints.ShapeOutline.StrokeWidth = 2f;

            float distanceYOffset;
            float nameXOffset = 7f * MainWindow.UIScale;
            float nameYOffset;

            if (heightDiff > HEIGHT_INDICATOR_THRESHOLD)
            {
                using var path = point.GetUpArrow(5);
                canvas.DrawPath(path, SKPaints.ShapeOutline);
                canvas.DrawPath(path, paints.Item1);
                distanceYOffset = 18f * MainWindow.UIScale;
                nameYOffset = 6f * MainWindow.UIScale;
            }
            else if (heightDiff < -HEIGHT_INDICATOR_THRESHOLD)
            {
                using var path = point.GetDownArrow(5);
                canvas.DrawPath(path, SKPaints.ShapeOutline);
                canvas.DrawPath(path, paints.Item1);
                distanceYOffset = 12f * MainWindow.UIScale;
                nameYOffset = 1f * MainWindow.UIScale;
            }
            else
            {
                var size = 5 * MainWindow.UIScale;
                canvas.DrawCircle(point, size, SKPaints.ShapeOutline);
                canvas.DrawCircle(point, size, paints.Item1);
                distanceYOffset = 16f * MainWindow.UIScale;
                nameYOffset = 4f * MainWindow.UIScale;
            }

            if (entitySettings.ShowName)
            {
                point.Offset(nameXOffset, nameYOffset);
                if (!string.IsNullOrEmpty(label))
                {
                    canvas.DrawText(label, point, SKPaints.TextOutline);
                    canvas.DrawText(label, point, paints.Item2);
                }
            }

            if (entitySettings.ShowDistance)
            {
                var distText = $"{(int)dist}m";
                var distWidth = paints.Item2.MeasureText($"{(int)dist}");
                var distPoint = new SKPoint(
                    point.X - (distWidth / 2) - nameXOffset,
                    point.Y + distanceYOffset - nameYOffset
                );
                canvas.DrawText(distText, distPoint, SKPaints.TextOutline);
                canvas.DrawText(distText, distPoint, paints.Item2);
            }
        }

        private Vector3 _position;
        public ref Vector3 Position => ref _position;
        public Vector2 MouseoverPosition { get; set; }

        public virtual void DrawESP(SKCanvas canvas, LocalPlayer localPlayer)
        {
            EntityTypeSettingsESP espSettings = LootESPSettings;

            var dist = Vector3.Distance(localPlayer.Position, Position);
            if (dist > espSettings.RenderDistance)
                return;

            if (!CameraManagerBase.WorldToScreen(ref _position, out var scrPos))
                return;

            var paints = GetESPPaints();
            var label = GetEntityUILabel(espSettings);
            var scale = ESP.Config.FontScale;

            switch (espSettings.RenderMode)
            {
                case EntityRenderMode.None:
                    break;

                case EntityRenderMode.Dot:
                    var dotSize = 3f * scale;
                    canvas.DrawCircle(scrPos.X, scrPos.Y, dotSize, paints.Item1);
                    break;

                case EntityRenderMode.Cross:
                    var crossSize = 5f * scale;

                    using (var thickPaint = new SKPaint
                    {
                        Color = paints.Item1.Color,
                        StrokeWidth = 1.5f * scale,
                        IsAntialias = true,
                        Style = SKPaintStyle.Stroke
                    })
                    {
                        canvas.DrawLine(
                            scrPos.X - crossSize, scrPos.Y - crossSize,
                            scrPos.X + crossSize, scrPos.Y + crossSize,
                            thickPaint);
                        canvas.DrawLine(
                            scrPos.X - crossSize, scrPos.Y + crossSize,
                            scrPos.X + crossSize, scrPos.Y - crossSize,
                            thickPaint);
                    }
                    break;

                case EntityRenderMode.Plus:
                    var plusSize = 5f * scale;

                    using (var thickPaint = new SKPaint
                    {
                        Color = paints.Item1.Color,
                        StrokeWidth = 1.5f * scale,
                        IsAntialias = true,
                        Style = SKPaintStyle.Stroke
                    })
                    {
                        canvas.DrawLine(
                            scrPos.X, scrPos.Y - plusSize,
                            scrPos.X, scrPos.Y + plusSize,
                            thickPaint);
                        canvas.DrawLine(
                            scrPos.X - plusSize, scrPos.Y,
                            scrPos.X + plusSize, scrPos.Y,
                            thickPaint);
                    }
                    break;

                case EntityRenderMode.Square:
                    var boxHalf = 3f * scale;
                    var boxPt = new SKRect(
                        scrPos.X - boxHalf, scrPos.Y - boxHalf,
                        scrPos.X + boxHalf, scrPos.Y + boxHalf);
                    canvas.DrawRect(boxPt, paints.Item1);
                    break;

                case EntityRenderMode.Diamond:
                default:
                    var diamondSize = 3.5f * scale;
                    using (var diamondPath = new SKPath())
                    {
                        diamondPath.MoveTo(scrPos.X, scrPos.Y - diamondSize);
                        diamondPath.LineTo(scrPos.X + diamondSize, scrPos.Y);
                        diamondPath.LineTo(scrPos.X, scrPos.Y + diamondSize);
                        diamondPath.LineTo(scrPos.X - diamondSize, scrPos.Y);
                        diamondPath.Close();
                        canvas.DrawPath(diamondPath, paints.Item1);
                    }
                    break;
            }

            if (espSettings.ShowName || espSettings.ShowDistance)
            {
                var textY = scrPos.Y + 16f * scale;
                var textPt = new SKPoint(scrPos.X, textY);

                textPt.DrawESPText(
                    canvas,
                    this,
                    localPlayer,
                    espSettings.ShowDistance,
                    paints.Item2,
                    espSettings.ShowName ? label : null
                );
            }
        }

        public virtual void DrawMouseover(SKCanvas canvas, LoneMapParams mapParams, LocalPlayer localPlayer)
        {
            if (this is LootContainer container)
            {
                var lines = new List<string>();
                var loot = container.FilteredLoot;
                if (loot is not null && loot.Count() > 1) // draw regular container loot
                {
                    foreach (var item in loot)
                        lines.Add(item.GetUILabel());
                }
                else
                {
                    return; // Don't draw single items
                }

                Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams).DrawMouseoverText(canvas, lines);
            }
            else if (this is LootItem lootItem)
            {
                var lines = new List<string>();

                lines.Add($"{lootItem.Name}");

                Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams).DrawMouseoverText(canvas, lines);
            }
        }

        public static void ApplyItemChams(ulong interactiveClass, int desiredMaterialId)
        {
            try
            {
                if (interactiveClass == 0)
                {
                    LoneLogging.WriteLine("[ApplyItemChams] Skipped: interactiveClass is 0");
                    return;
                }

                var rendererList = Memory.ReadPtr(interactiveClass + 0x90);
                if (rendererList == 0)
                {
                    LoneLogging.WriteLine($"[ApplyItemChams] Skipped: rendererList is 0 for {interactiveClass:X}");
                    return;
                }

                int rendererCount = Memory.ReadValue<int>(rendererList + 0x18);
                if (rendererCount <= 0 || rendererCount > 1000)
                {
                    LoneLogging.WriteLine($"[ApplyItemChams] Skipped: invalid rendererCount ({rendererCount}) for {interactiveClass:X}");
                    return;
                }

                var rendererBase = Memory.ReadPtr(rendererList + 0x10);
                if (rendererBase == 0)
                {
                    LoneLogging.WriteLine($"[ApplyItemChams] Skipped: rendererBase is 0 for {interactiveClass:X}");
                    return;
                }

                for (int i = 0; i < rendererCount; i++)
                {
                    var renderer = Memory.ReadPtr(rendererBase + 0x20 + (ulong)(i * 0x8));
                    if (renderer == 0) continue;

                    var materialDict = Memory.ReadPtr(renderer + 0x10);
                    if (materialDict == 0) continue;

                    int matCount = Memory.ReadValue<int>(materialDict + 0x158);
                    if (matCount <= 0 || matCount > 100)
                    {
                        LoneLogging.WriteLine($"[ApplyItemChams] Skipped: invalid matCount ({matCount}) at {materialDict:X}");
                        continue;
                    }

                    var matArray = Memory.ReadPtr(materialDict + 0x148);
                    if (matArray == 0)
                    {
                        LoneLogging.WriteLine($"[ApplyItemChams] Skipped: matArray is 0 at {materialDict:X}");
                        continue;
                    }

                    for (int j = 0; j < matCount; j++)
                    {
                        Memory.WriteValue(matArray + (ulong)(j * 0x4), desiredMaterialId);
                    }
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[ApplyItemChams] Failed for {interactiveClass:X}: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets a UI Friendly Label.
        /// </summary>
        /// <param name="showPrice">Show price in label.</param>
        /// <param name="showImportant">Show Important !! in label.</param>
        /// <returns>Item Label string cleaned up for UI usage.</returns>
        public string GetUILabel()
        {
            var label = "";
            if (this is LootContainer container)
                label = container.Name;
            else
                label += ShortName;

            if (string.IsNullOrEmpty(label))
                label = "Item";

            return label;
        }

        private string GetEntityUILabel(EntityTypeSettings settings)
        {
            var label = "";

            if (this is LootContainer container)
            {
                var loot = container.FilteredLoot;

                if (settings.ShowName)
                {
                    if (loot.Count() == 1)
                    {
                        var firstItem = loot.First();

                        if (settings.ShowName)
                            label = firstItem.ShortName;
                    }
                    else
                    {
                        label = container.Name;
                    }
                }
            }
            else
            {
                if (settings.ShowName)
                    label = ShortName;
            }

            return label;
        }

        private string GetEntityUILabel(EntityTypeSettingsESP settings)
        {
            var label = "";
            if (this is LootContainer container)
            {
                var loot = container.FilteredLoot;

                if (settings.ShowName)
                {
                    if (loot.Count() == 1)
                    {
                        var firstItem = loot.First();

                        if (settings.ShowName)
                            label = firstItem.ShortName;
                    }
                    else
                    {
                        label = container.Name;
                    }
                }
            }
            else
            {
                if (settings.ShowName)
                    label = ShortName;
            }

            return label;
        }

        private ValueTuple<SKPaint, SKPaint> GetPaints()
        {
            if (Config.ShowThrowables && IsThrowableWeapon)
                return new(SKPaints.PaintThrowableLoot, SKPaints.TextThrowableLoot);
            if (Config.ShowWeapons && IsWeapon)
                return new(SKPaints.PaintWeaponLoot, SKPaints.TextWeaponLoot);
            if (Config.ShowMeds && IsMeds)
                return new(SKPaints.PaintMeds, SKPaints.TextMeds);
            if (Config.ShowBackpacks && IsBackpack)
                return new(SKPaints.PaintBackpacks, SKPaints.TextBackpacks);

            return new(SKPaints.PaintDefaultLoot, SKPaints.TextDefaultLoot);
        }

        private ValueTuple<SKPaint, SKPaint> GetESPPaints()
        {
            if (Config.ShowThrowables && IsThrowableWeapon)
                return new(SKPaints.PaintThrowableLootESP, SKPaints.TextThrowableLootESP);
            if (Config.ShowWeapons && IsWeapon)
                return new(SKPaints.PaintWeaponLootESP, SKPaints.TextWeaponLootESP);
            if (Config.ShowMeds && IsMeds)
                return new(SKPaints.PaintMedsESP, SKPaints.TextMedsESP);
            if (Config.ShowBackpacks && IsBackpack)
                return new(SKPaints.PaintBackpacksESP, SKPaints.TextBackpacksESP);

            return new(SKPaints.PaintDefaultLootESP, SKPaints.TextDefaultLootESP);
        }

        public SKPaint GetMiniRadarPaint()
        {
            if (Config.ShowThrowables && IsThrowableWeapon)
                return SKPaints.PaintMiniThrowableLoot;
            if (Config.ShowWeapons && IsWeapon)
                return SKPaints.PaintMiniWeaponLoot;
            if (Config.ShowMeds && IsMeds)
                return SKPaints.PaintMiniMeds;
            if (Config.ShowBackpacks && IsBackpack)
                return SKPaints.PaintMiniBackpacks;

            return SKPaints.PaintMiniLoot;
        }
    }

    public static class LootItemExtensions
    {
        /// <summary>
        /// Order loot (important first, then by price).
        /// </summary>
        /// <param name="loot"></param>
        /// <returns>Ordered loot.</returns>
        public static IEnumerable<LootItem> OrderLoot(this IEnumerable<LootItem> loot)
        {
            return loot
                .OrderByDescending(x => x.IsBackpack);
        }
    }
}
