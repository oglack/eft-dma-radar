using arena_dma_radar.Arena.ArenaPlayer.Plugins;
using arena_dma_radar.Arena.ArenaPlayer.SpecialCollections;
using arena_dma_radar.Arena.Features;
using arena_dma_radar.Arena.Features.MemoryWrites;
using arena_dma_radar.Arena.GameWorld;
using arena_dma_radar.UI.ESP;
using arena_dma_radar.UI.Misc;
using arena_dma_radar.UI.Radar;
using eft_dma_shared.Common.DMA;
using eft_dma_shared.Common.DMA.ScatterAPI;
using eft_dma_shared.Common.ESP;
using eft_dma_shared.Common.Features;
using eft_dma_shared.Common.Maps;
using eft_dma_shared.Common.Misc;
using eft_dma_shared.Common.Misc.Data;
using eft_dma_shared.Common.Misc.Pools;
using eft_dma_shared.Common.Players;
using eft_dma_shared.Common.Unity;
using eft_dma_shared.Common.Unity.Collections;
using eft_dma_shared.Common.Unity.LowLevel;
using System;

namespace arena_dma_radar.Arena.ArenaPlayer
{
    /// <summary>
    /// Base class for Tarkov Players.
    /// Tarkov implements several distinct classes that implement a similar player interface.
    /// </summary>
    public abstract class Player : IWorldEntity, IMapEntity, IMouseoverEntity, IESPEntity, IPlayer
    {
        #region Static Interfaces
        public static implicit operator ulong(Player x) => x?.Base ?? 0x0;
        private static readonly ConcurrentDictionary<ulong, Stopwatch> _rateLimit = new();

        /// <summary>
        /// Player History Log.
        /// </summary>
        public static PlayerHistory PlayerHistory { get; } = new();

        /// <summary>
        /// Player Watchlist Entries.
        /// </summary>
        public static PlayerWatchlist PlayerWatchlist { get; } = new();

        /// <summary>
        /// Resets/Updates 'static' assets in preparation for a new game/raid instance.
        /// </summary>
        public static void Reset()
        {
            _rateLimit.Clear();
            PlayerHistory.Reset();

            lock (_focusedPlayers)
            {
                _focusedPlayers.Clear();
            }
        }
        #endregion

        #region Allocation
        /// <summary>
        /// Allocates a player and takes into consideration any rate-limits.
        /// </summary>
        /// <param name="playerDict">Player Dictionary collection to add the newly allocated player to.</param>
        /// <param name="playerBase">Player base memory address.</param>
        /// <param name="initialPosition">Initial position to be set (Optional). Usually for reallocations.</param>
        public static void Allocate(ConcurrentDictionary<ulong, Player> playerDict, ulong playerBase, Vector3? initialPosition = null)
        {
            var sw = _rateLimit.AddOrUpdate(playerBase,
                (key) => new(),
                (key, oldValue) => oldValue);
            if (sw.IsRunning && sw.Elapsed.TotalMilliseconds < 500f)
                return;
            try
            {
                var player = AllocateInternal(playerBase, initialPosition);
                playerDict[player] = player;
                LoneLogging.WriteLine($"Player '{player.Name}' allocated.");
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"ERROR during Player Allocation for player @ 0x{playerBase.ToString("X")}: {ex}");
            }
            finally
            {
                sw.Restart();
            }
        }

        private static Player AllocateInternal(ulong playerBase, Vector3? initialPosition)
        {
            var className = ObjectClass.ReadName(playerBase, 64);
            if (className != "ArenaObservedPlayerView")
                throw new ArgumentOutOfRangeException(nameof(className));
            return new ArenaObservedPlayer(playerBase);
        }

        /// <summary>
        /// Player Constructor.
        /// </summary>
        protected Player(ulong playerBase)
        {
            ArgumentOutOfRangeException.ThrowIfZero(playerBase, nameof(playerBase));
            Base = playerBase;
        }

        #endregion

        #region Fields / Properties
        const float HEIGHT_INDICATOR_THRESHOLD = 1.85f;
        const float HEIGHT_INDICATOR_ARROW_SIZE = 2f;

        /// <summary>
        /// Player Class Base Address
        /// </summary>
        public ulong Base { get; }
        /// <summary>
        /// True if the Player is Active (in the player list).
        /// </summary>
        public bool IsActive { get; private set; }
        /// <summary>
        /// Type of player unit.
        /// </summary>
        public PlayerType Type { get; protected set; }
        /// <summary>
        /// Streaming platform username.
        /// </summary>
        public string StreamingUsername { get; set; }
        /// <summary>
        /// The streaming platform URL they're streaming
        /// </summary>
        public string StreamingURL { get; set; }
        /// <summary>
        /// Player's Rotation in Local Game World.
        /// </summary>
        public Vector2 Rotation { get; private set; }
        /// <summary>
        /// Player's Map Rotation (with 90 degree correction applied).
        /// </summary>
        public float MapRotation
        {
            get
            {
                float mapRotation = Rotation.X; // Cache value
                mapRotation -= 90f;
                while (mapRotation < 0f)
                    mapRotation += 360f;

                return mapRotation;
            }
        }
        /// <summary>
        /// Corpse field value.
        /// </summary>
        public ulong? Corpse { get; private set; }
        /// <summary>
        /// Stopwatch for High Alert ESP Feature.
        /// </summary>
        public Stopwatch HighAlertSw { get; } = new();
        /// <summary>
        /// Player's Skeleton Bones.
        /// </summary>
        public virtual Skeleton Skeleton => throw new NotImplementedException(nameof(Skeleton));
        /// <summary>
        /// Duration of consecutive errors.
        /// </summary>
        public Stopwatch ErrorTimer { get; } = new();

        /// <summary>
        /// Player's Gear/Loadout Information and contained items.
        /// </summary>
        public GearManager Gear { get; private set; }

        /// <summary>
        /// Contains information about the item/weapons in Player's hands.
        /// </summary>
        public HandsManager Hands { get; private set; }

        /// <summary>
        /// True if player is 'Locked On' via Aimbot.
        /// </summary>
        public bool IsAimbotLocked
        {
            get => _isAimbotLocked;
            set
            {
                if (_isAimbotLocked != value)
                {
                    _isAimbotLocked = value;

                    if (value && Memory.Game is LocalGameWorld game)
                        PlayerChamsManager.ApplyAimbotChams(this, game);
                    else if (!value && Memory.Game is LocalGameWorld game2)
                        PlayerChamsManager.RemoveAimbotChams(this, game2, true);
                }
            }
        }

        /// <summary>
        /// True if Player is Focused (displays a different color on Radar/ESP).
        /// </summary>
        public bool IsFocused { get; set; }

        /// <summary>
        /// True if the player is streaming
        /// </summary>
        public bool IsStreaming { get; set; }

        /// <summary>
        /// Alerts for this Player Object.
        /// Used by Player History UI Interop.
        /// </summary>
        public string Alerts { get; private set; }

        public Vector2 MouseoverPosition { get; set; }
        public bool IsAiming { get; set; } = false;
        private bool _isAimbotLocked;

        #endregion

        #region Virtual Properties
        /// <summary>
        /// Player name.
        /// </summary>
        public virtual string Name { get; set; }
        /// <summary>
        /// Account UUID for Human Controlled Players.
        /// </summary>
        public virtual string AccountID { get; }
        /// <summary>
        /// Group that the player belongs to.
        /// </summary>
        public virtual int TeamID { get; } = -1;
        /// <summary>
        /// Player's Faction.
        /// </summary>
        public virtual Enums.EPlayerSide PlayerSide { get; }
        /// <summary>
        /// Player is Human-Controlled.
        /// </summary>
        public virtual bool IsHuman { get; }
        /// <summary>
        /// MovementContext / StateContext
        /// </summary>
        public virtual ulong MovementContext { get; }
        /// <summary>
        /// EFT.PlayerBody
        /// </summary>
        public virtual ulong Body { get; }
        /// <summary>
        /// Inventory Controller field address.
        /// </summary>
        public virtual ulong InventoryControllerAddr { get; }
        /// <summary>
        /// Hands Controller field address.
        /// </summary>
        public virtual ulong HandsControllerAddr { get; }
        /// <summary>
        /// Corpse field address..
        /// </summary>
        public virtual ulong CorpseAddr { get; }
        /// <summary>
        /// Player Rotation Field Address (view angles).
        /// </summary>
        public virtual ulong RotationAddress { get; }

        public virtual ref Vector3 Position => ref this.Skeleton.Root.Position;
        #endregion

        #region Boolean Getters
        /// <summary>
        /// Player is a PMC Operator.
        /// </summary>
        public bool IsPmc => PlayerSide is Enums.EPlayerSide.Usec || PlayerSide is Enums.EPlayerSide.Bear;
        /// <summary>
        /// Player is alive (not dead).
        /// </summary>
        public bool IsAlive => Corpse is null;
        /// <summary>
        /// True if Player is Friendly to LocalPlayer.
        /// </summary>
        public bool IsFriendly => this is LocalPlayer || Type is PlayerType.Teammate;
        /// <summary>
        /// True if player is Hostile to LocalPlayer.
        /// </summary>
        public bool IsHostile => !IsFriendly;
        /// <summary>
        /// Player is hostile and alive/active.
        /// </summary>
        public bool IsHostileActive => IsHostile && IsActive && IsAlive;
        /// <summary>
        /// Player is friendly and alive/active.
        /// </summary>
        public bool IsFriendlyActive => IsFriendly && IsActive && IsAlive;
        /// <summary>
        /// Player is human-controlled, hostile, and Active/Alive.
        /// </summary>
        public bool IsHumanActive => IsHuman && IsActive && IsAlive;
        /// <summary>
        /// Player is human-controlled & Hostile.
        /// </summary>
        public bool IsHumanHostile => IsHuman && IsHostile;
        /// <summary>
        /// Player is human-controlled, hostile, and Active/Alive.
        /// </summary>
        public bool IsHumanHostileActive => IsHumanHostile && IsActive && IsAlive;
        /// <summary>
        /// Player is human-controlled (Not LocalPlayer).
        /// </summary>
        public bool IsHumanOther => IsHuman && this is not LocalPlayer;
        /// <summary>
        /// Player is friendly to LocalPlayer (including LocalPlayer) and Active/Alive.
        /// </summary>
        public bool HasExfild => !IsActive && IsAlive;

        private static Config Config => Program.Config;

        private bool BattleMode => Config.BattleMode;
        #endregion

        #region Methods

        private readonly Lock _alertsLock = new();
        /// <summary>
        /// Update the Alerts for this Player Object.
        /// </summary>
        /// <param name="alert">Alert to set.</param>
        public void UpdateAlerts(string alert)
        {
            if (alert is null)
                return;

            lock (_alertsLock)
            {
                if (this.Alerts is null)
                    this.Alerts = alert;
                else
                    this.Alerts = $"{alert} | {this.Alerts}";
            }
        }

        public void ClearAlerts()
        {
            lock (_alertsLock)
            {
                this.Alerts = null;
            }
        }

        public void UpdatePlayerType(PlayerType newType)
        {
            this.Type = newType;
        }

        public void UpdateStreamingUsername(string url)
        {
            this.StreamingUsername = url;
        }

        /// <summary>
        /// Validates the Rotation Address.
        /// </summary>
        /// <param name="rotationAddr">Rotation va</param>
        /// <returns>Validated rotation virtual address.</returns>
        protected static ulong ValidateRotationAddr(ulong rotationAddr)
        {
            var rotation = Memory.ReadValue<Vector2>(rotationAddr, false);
            if (!rotation.IsNormalOrZero() ||
                Math.Abs(rotation.X) > 360f ||
                Math.Abs(rotation.Y) > 90f)
                throw new ArgumentOutOfRangeException(nameof(rotationAddr));

            return rotationAddr;
        }

        /// <summary>
        /// Refreshes non-realtime player information. Call in the Registered Players Loop (T0).
        /// </summary>
        /// <param name="index"></param>
        /// <param name="registered"></param>
        /// <param name="isActiveParam"></param>
        public virtual void OnRegRefresh(ScatterReadIndex index, IReadOnlySet<ulong> registered, bool? isActiveParam = null)
        {
            if (isActiveParam is not bool isActive)
                isActive = registered.Contains(this);
            if (isActive)
            {
                this.SetAlive();
            }
            else if (this.IsAlive) // Not in list, but alive
            {
                index.AddEntry<ulong>(0, this.CorpseAddr);
                index.Callbacks += x1 =>
                {
                    if (x1.TryGetResult<ulong>(0, out var corpsePtr) && corpsePtr != 0x0)
                        this.SetDead(corpsePtr);
                    else
                        this.SetInactive();
                };
            }
        }

        /// <summary>
        /// Mark player as inactive (not dead / not on map).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetInactive()
        {
            Corpse = null;
            IsActive = false;
        }

        /// <summary>
        /// Mark player as dead.
        /// </summary>
        /// <param name="corpse">Corpse address.</param>
        public void SetDead(ulong corpse)
        {
            if (Memory.Game is LocalGameWorld game && Config.ChamsConfig.Enabled)
                PlayerChamsManager.ApplyDeathMaterial(this, game);

            Corpse = corpse;
            IsActive = false;
        }

        /// <summary>
        /// Mark player as alive.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetAlive()
        {
            Corpse = null;
            IsActive = true;
        }

        /// <summary>
        /// Executed on each Realtime Loop.
        /// </summary>
        /// <param name="index">Scatter read index dedicated to this player.</param>
        public virtual void OnRealtimeLoop(ScatterReadIndex index)
        {
            index.AddEntry<Vector2>(-1, this.RotationAddress); // Rotation
            foreach (var tr in Skeleton.Bones)
            {
                index.AddEntry<SharedArray<UnityTransform.TrsX>>((int)(uint)tr.Key, tr.Value.VerticesAddr,
                    (3 * tr.Value.Index + 3) * 16); // ESP Vertices
            }
            index.Callbacks += x1 =>
            {
                bool p1 = false;
                bool p2 = true;
                if (x1.TryGetResult<Vector2>(-1, out var rotation))
                    p1 = this.SetRotation(ref rotation);
                foreach (var tr in Skeleton.Bones)
                {
                    if (x1.TryGetResult<SharedArray<UnityTransform.TrsX>>((int)(uint)tr.Key, out var vertices))
                    {
                        try
                        {
                            try
                            {
                                _ = tr.Value.UpdatePosition(vertices);
                            }
                            catch (Exception ex) // Attempt to re-allocate Transform on error
                            {
                                LoneLogging.WriteLine($"ERROR getting Player '{this.Name}' {tr.Key} Position: {ex}");
                                this.Skeleton.ResetTransform(tr.Key);
                            }
                        }
                        catch
                        {
                            p2 = false;
                        }
                    }
                    else
                    {
                        p2 = false;
                    }
                }

                if (p1 && p2)
                    this.ErrorTimer.Reset();
                else
                    this.ErrorTimer.Start();
            };
        }

        /// <summary>
        /// Executed on each Transform Validation Loop.
        /// </summary>
        /// <param name="round1">Index (round 1)</param>
        /// <param name="round2">Index (round 2)</param>
        public void OnValidateTransforms(ScatterReadIndex round1, ScatterReadIndex round2)
        {
            foreach (var tr in Skeleton.Bones)
            {
                round1.AddEntry<MemPointer>((int)(uint)tr.Key,
                    tr.Value.TransformInternal +
                    UnityOffsets.TransformInternal.TransformAccess); // Bone Hierarchy
                round1.Callbacks += x1 =>
                {
                    if (x1.TryGetResult<MemPointer>((int)(uint)tr.Key, out var tra))
                        round2.AddEntry<MemPointer>((int)(uint)tr.Key, tra + UnityOffsets.TransformAccess.Vertices); // Vertices Ptr
                    round2.Callbacks += x2 =>
                    {
                        if (x2.TryGetResult<MemPointer>((int)(uint)tr.Key, out var verticesPtr))
                        {
                            if (tr.Value.VerticesAddr != verticesPtr) // check if any addr changed
                            {
                                LoneLogging.WriteLine($"WARNING - '{tr.Key}' Transform has changed for Player '{this.Name}'");
                                this.Skeleton.ResetTransform(tr.Key); // alloc new transform
                            }
                        }
                    };
                };
            }
        }

        /// <summary>
        /// Set player rotation (Direction/Pitch)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected bool SetRotation(ref Vector2 rotation)
        {
            try
            {
                rotation.ThrowIfAbnormalAndNotZero();
                rotation.X = rotation.X.NormalizeAngle();
                ArgumentOutOfRangeException.ThrowIfLessThan(rotation.X, 0f);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(rotation.X, 360f);
                ArgumentOutOfRangeException.ThrowIfLessThan(rotation.Y, -90f);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(rotation.Y, 90f);
                Rotation = rotation;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Returns the Team ID of a player based on their Armband Color.
        /// </summary>
        /// <param name="inventoryController">Player's Inventory Controller.</param>
        /// <returns>Team ID. -1 if not found.</returns>
        protected static int GetTeamID(ulong inventoryController)
        {
            var inventory = Memory.ReadPtr(inventoryController + Offsets.InventoryController.Inventory);
            var equipment = Memory.ReadPtr(inventory + Offsets.Inventory.Equipment);
            var slots = Memory.ReadPtr(equipment + Offsets.Equipment.Slots);
            using var slotsArray = MemArray<ulong>.Get(slots);

            foreach (var slotPtr in slotsArray)
            {
                var slotNamePtr = Memory.ReadPtr(slotPtr + Offsets.Slot.ID);
                string name = Memory.ReadUnityString(slotNamePtr);
                if (name == "ArmBand")
                {
                    var containedItem = Memory.ReadPtr(slotPtr + Offsets.Slot.ContainedItem);
                    var itemTemplate = Memory.ReadPtr(containedItem + Offsets.LootItem.Template);
                    var idPtr = Memory.ReadValue<Types.MongoID>(itemTemplate + Offsets.ItemTemplate._id);
                    string id = Memory.ReadUnityString(idPtr.StringID);

                    if (id == "63615c104bc92641374a97c8")
                        return (int)Enums.ArmbandColorType.red;
                    else if (id == "63615bf35cb3825ded0db945")
                        return (int)Enums.ArmbandColorType.fuchsia;
                    else if (id == "63615c36e3114462cd79f7c1")
                        return (int)Enums.ArmbandColorType.yellow;
                    else if (id == "63615bfc5cb3825ded0db947")
                        return (int)Enums.ArmbandColorType.green;
                    else if (id == "63615bc6ff557272023d56ac")
                        return (int)Enums.ArmbandColorType.azure;
                    else if (id == "63615c225cb3825ded0db949")
                        return (int)Enums.ArmbandColorType.white;
                    else if (id == "63615be82e60050cb330ef2f")
                        return (int)Enums.ArmbandColorType.blue;
                    else
                        return -1;
                }
            }

            return -1;
        }

        /// <summary>
        /// Get the Transform Internal Chain for this Player.
        /// </summary>
        /// <param name="bone">Bone to lookup.</param>
        /// <returns>Array of offsets for transform internal chain.</returns>
        public virtual uint[] GetTransformInternalChain(Bones bone) =>
            throw new NotImplementedException();
        #endregion

        #region Interfaces

        public void Draw(SKCanvas canvas, LoneMapParams mapParams, ILocalPlayer localPlayer)
        {
            try
            {
                var playerTypeKey = DeterminePlayerTypeKey();
                var typeSettings = Config.PlayerTypeSettings.GetSettings(playerTypeKey);
                var dist = Vector3.Distance(localPlayer.Position, Position);

                if (dist > typeSettings.RenderDistance)
                    return;

                var mapPosition = Position.ToMapPos(mapParams.Map);
                var point = mapPosition.ToZoomedPos(mapParams);
                MouseoverPosition = new Vector2(point.X, point.Y);

                if (!IsAlive)
                {
                    if (Config.DeathMarkers)
                        DrawDeathMarker(canvas, point);

                    return;
                }

                DrawPlayerMarker(canvas, localPlayer, point, typeSettings);

                if (this == localPlayer || BattleMode)
                    return;

                var observedPlayer = this as ArenaObservedPlayer;
                var height = Position.Y - localPlayer.Position.Y;
                string nameText = null;
                string distanceText = null;
                string heightText = null;
                var rightSideInfo = new List<string>();

                if (typeSettings.ShowName)
                {
                    var name = ErrorTimer.ElapsedMilliseconds > 100 ? "ERROR" : (Config.MaskNames && IsHuman ? "<Hidden>" : Name);
                    nameText = $"{name}";
                }

                if (typeSettings.ShowDistance)
                    distanceText = $"{(int)Math.Round(dist)}";

                if (typeSettings.ShowHeight && !typeSettings.HeightIndicator)
                    heightText = $"{(int)Math.Round(height)}";

                if (observedPlayer != null)
                {
                    if (LocalGameWorld.MatchMode == Enums.ERaidMode.BlastGang)
                        if (typeSettings.ShowBomb && observedPlayer.Gear?.HasBomb == true)
                            rightSideInfo.Add("B");
                    if (typeSettings.ShowHealth && observedPlayer.HealthStatus != Enums.ETagStatus.Healthy)
                        rightSideInfo.Add($"{observedPlayer.HealthStatus.GetDescription()}");
                    if (typeSettings.ShowLevel && observedPlayer.Profile?.Level is int playerLevel)
                        rightSideInfo.Add($"L: {playerLevel}");
                    if (typeSettings.ShowKD && observedPlayer.Profile?.Overall_KD is float kd)
                        if (kd >= typeSettings.MinKD)
                            rightSideInfo.Add(kd.ToString("n2"));
                    if (typeSettings.ShowADS && IsAiming)
                        rightSideInfo.Add("ADS");
                    if (typeSettings.ShowWeapon && observedPlayer.Hands?.CurrentItem != null)
                        rightSideInfo.Add(observedPlayer.Hands.CurrentItem);
                    if (typeSettings.ShowAmmoType && observedPlayer.Hands?.CurrentAmmo != null)
                        rightSideInfo.Add($"{observedPlayer.Hands.CurrentAmmo}");
                }

                if (typeSettings.ShowTag && !string.IsNullOrEmpty(Alerts))
                    rightSideInfo.Add(Alerts);

                DrawPlayerText(canvas, point, nameText, distanceText, heightText, rightSideInfo);

                if (typeSettings.ShowHeight && typeSettings.HeightIndicator)
                    DrawAlternateHeightIndicator(canvas, point, height, GetPaints());
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"WARNING! Player Draw Error: {ex}");
            }
        }

        private void DrawAlternateHeightIndicator(SKCanvas canvas, SKPoint point, float heightDiff, ValueTuple<SKPaint, SKPaint> paints)
        {
            var baseX = point.X - (15.0f * MainWindow.UIScale);
            var baseY = point.Y + (3.5f * MainWindow.UIScale);

            SKPaints.ShapeOutline.StrokeWidth = 2f * MainWindow.UIScale;

            var arrowSize = HEIGHT_INDICATOR_ARROW_SIZE * MainWindow.UIScale;
            var circleSize = arrowSize * 0.7f;

            if (heightDiff > HEIGHT_INDICATOR_THRESHOLD)
            {
                var upArrowPoint = new SKPoint(baseX, baseY - arrowSize);
                using var path = upArrowPoint.GetUpArrow(arrowSize);
                canvas.DrawPath(path, SKPaints.ShapeOutline);
                canvas.DrawPath(path, paints.Item1);
            }
            else if (heightDiff < -HEIGHT_INDICATOR_THRESHOLD)
            {
                var downArrowPoint = new SKPoint(baseX, baseY - arrowSize / 2);
                using var path = downArrowPoint.GetDownArrow(arrowSize);
                canvas.DrawPath(path, SKPaints.ShapeOutline);
                canvas.DrawPath(path, paints.Item1);
            }
        }

        private void DrawPlayerText(SKCanvas canvas, SKPoint point,
                                      string nameText, string distanceText,
                                      string heightText, List<string> rightSideInfo)
        {
            var paints = GetPaints();

            if (MainWindow.MouseoverGroup is int teamID && teamID == TeamID)
                paints.Item2 = SKPaints.TextMouseoverGroup;

            var spacing = 3 * MainWindow.UIScale;
            var textSize = 12 * MainWindow.UIScale;
            var baseYPosition = point.Y - 12 * MainWindow.UIScale;

            var playerTypeKey = DeterminePlayerTypeKey();
            var typeSettings = Config.PlayerTypeSettings.GetSettings(playerTypeKey);

            if (!string.IsNullOrEmpty(nameText))
            {
                var nameWidth = paints.Item2.MeasureText(nameText);
                var namePoint = new SKPoint(point.X - (nameWidth / 2), baseYPosition - 0);

                canvas.DrawText(nameText, namePoint, SKPaints.TextOutline);
                canvas.DrawText(nameText, namePoint, paints.Item2);
            }

            if (!string.IsNullOrEmpty(distanceText))
            {
                var distWidth = paints.Item2.MeasureText(distanceText);
                var distPoint = new SKPoint(point.X - (distWidth / 2), point.Y + 20 * MainWindow.UIScale);

                canvas.DrawText(distanceText, distPoint, SKPaints.TextOutline);
                canvas.DrawText(distanceText, distPoint, paints.Item2);
            }

            if (!string.IsNullOrEmpty(heightText))
            {
                var heightWidth = paints.Item2.MeasureText(heightText);
                var heightPoint = new SKPoint(point.X - heightWidth - 15 * MainWindow.UIScale, point.Y + 5 * MainWindow.UIScale);

                canvas.DrawText(heightText, heightPoint, SKPaints.TextOutline);
                canvas.DrawText(heightText, heightPoint, paints.Item2);
            }

            if (rightSideInfo.Count > 0)
            {
                var rightPoint = new SKPoint(
                    point.X + 14 * MainWindow.UIScale,
                    point.Y + 2 * MainWindow.UIScale
                );

                foreach (var line in rightSideInfo)
                {
                    if (string.IsNullOrEmpty(line?.Trim()))
                        continue;

                    canvas.DrawText(line, rightPoint, SKPaints.TextOutline);
                    canvas.DrawText(line, rightPoint, paints.Item2);
                    rightPoint.Offset(0, textSize);
                }
            }
        }

        /// <summary>
        /// Draws a Player Marker on this location with type-specific settings
        /// </summary>
        private void DrawPlayerMarker(SKCanvas canvas, ILocalPlayer localPlayer, SKPoint point, PlayerTypeSettings typeSettings)
        {
            var radians = MapRotation.ToRadians();
            var paints = GetPaints();

            if (this != localPlayer && MainWindow.MouseoverGroup is int grp && grp == TeamID)
                paints.Item1 = SKPaints.PaintMouseoverGroup;

            SKPaints.ShapeOutline.StrokeWidth = paints.Item1.StrokeWidth + 2f * MainWindow.UIScale;

            var size = 6 * MainWindow.UIScale;
            canvas.DrawCircle(point, size, SKPaints.ShapeOutline);
            canvas.DrawCircle(point, size, paints.Item1);

            var aimlineLength = typeSettings.AimlineLength;

            if (typeSettings.HighAlert && !IsFriendly && this.IsFacingTarget(localPlayer, typeSettings.RenderDistance))
                aimlineLength = 9999;

            var aimlineEnd = GetAimlineEndpoint(point, radians, aimlineLength);
            canvas.DrawLine(point, aimlineEnd, SKPaints.ShapeOutline);
            canvas.DrawLine(point, aimlineEnd, paints.Item1);
        }

        /// <summary>
        /// Draws a Death Marker on this location.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DrawDeathMarker(SKCanvas canvas, SKPoint point)
        {
            var length = 6 * MainWindow.UIScale;
            canvas.DrawLine(new SKPoint(point.X - length, point.Y + length),
                new SKPoint(point.X + length, point.Y - length), SKPaints.PaintDeathMarker);
            canvas.DrawLine(new SKPoint(point.X - length, point.Y - length),
                new SKPoint(point.X + length, point.Y + length), SKPaints.PaintDeathMarker);
        }

        /// <summary>
        /// Gets the point where the Aimline 'Line' ends. Applies UI Scaling internally.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static SKPoint GetAimlineEndpoint(SKPoint start, float radians, float aimlineLength)
        {
            aimlineLength *= MainWindow.UIScale;
            return new SKPoint(start.X + MathF.Cos(radians) * aimlineLength,
                start.Y + MathF.Sin(radians) * aimlineLength);
        }

        private void ApplyAimbotChams()
        {
            if (Memory.Game is LocalGameWorld game)
                PlayerChamsManager.ApplyAimbotChams(this, game);
        }

        private ValueTuple<SKPaint, SKPaint> GetPaints()
        {
            if (IsAimbotLocked)
                return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintAimbotLocked, SKPaints.TextAimbotLocked);

            if (IsFocused)
                return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintFocused, SKPaints.TextFocused);

            if (this is LocalPlayer)
                return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintLocalPlayer, SKPaints.TextLocalPlayer);

            switch (Type)
            {
                case PlayerType.Teammate:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintTeammate, SKPaints.TextTeammate);
                case PlayerType.USEC:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintUSEC, SKPaints.TextUSEC);
                case PlayerType.BEAR:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintBEAR, SKPaints.TextBEAR);
                case PlayerType.AI:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintAI, SKPaints.TextAI);
                case PlayerType.SpecialPlayer:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintSpecial, SKPaints.TextSpecial);
                case PlayerType.Streamer:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintStreamer, SKPaints.TextStreamer);
                default:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintUSEC, SKPaints.TextUSEC);
            }
        }

        public void DrawMouseover(SKCanvas canvas, LoneMapParams mapParams, LocalPlayer localPlayer)
        {
            var playerTypeKey = DeterminePlayerTypeKey();
            var typeSettings = Config.PlayerTypeSettings.GetSettings(playerTypeKey);

            var lines = new List<string>();
            var name = Config.MaskNames && IsHuman ? "<Hidden>" : Name;

            if (IsStreaming) // Streamer Notice
                lines.Add($"[LIVE - Double Click]");

            var alert = this.Alerts?.Trim();

            if (!string.IsNullOrEmpty(alert)) // Special Players,etc.
                lines.Add(alert);

            if (this is ArenaObservedPlayer observed)
            {
                var health = observed.HealthStatus is Enums.ETagStatus.Healthy
                        ? null
                        : $" ({observed.HealthStatus.GetDescription()})"; // Only display abnormal health status

                lines.Add($"{name}{health}");
                if (observed.Profile?.Overall_KD is float kdResult)
                    lines.Add($"KD: {kdResult.ToString("n2")}");
                var hands = $"{observed.Hands?.CurrentItem} {observed.Hands?.CurrentAmmo}".Trim();
                lines.Add($"Use: {(hands is null ? "--" : hands)}");

                if (TeamID != -1)
                    lines.Add($"T:{observed.TeamID} ");

                var equipment = observed.Gear?.Equipment;
                if (equipment is not null)
                {
                    foreach (var item in equipment)
                        lines.Add($"{item.Key}: {item.Value}");
                }
            }
            else
                return;

            Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams).DrawMouseoverText(canvas, lines);
        }

        public void DrawESP(SKCanvas canvas, LocalPlayer localPlayer)
        {
            if (this == localPlayer || !IsActive || !IsAlive)
                return;

            var playerTypeKey = DeterminePlayerTypeKey();
            var espTypeSettings = ESP.Config.PlayerTypeESPSettings.GetSettings(playerTypeKey);

            var dist = Vector3.Distance(localPlayer.Position, Position);
            if (dist > espTypeSettings.RenderDistance)
                return;

            var renderMode = espTypeSettings.RenderMode;
            var highAlert = espTypeSettings.HighAlert;

            if (IsHostile && highAlert)
            {
                if (this.IsFacingTarget(localPlayer))
                {
                    if (!HighAlertSw.IsRunning)
                        HighAlertSw.Start();
                    else if (HighAlertSw.Elapsed.TotalMilliseconds >= 500f) // Don't draw twice or more
                        HighAlert.DrawHighAlertESP(canvas, this);
                }
                else
                {
                    HighAlertSw.Reset();
                }
            }

            if (!CameraManagerBase.WorldToScreen(ref Position, out var baseScrPos))
                return;

            var espPaints = GetESPPaints();

            SKRect? playerBox = null;
            var calcBox = Skeleton.GetESPBox(baseScrPos);
            if (calcBox is SKRect box)
                playerBox = box;

            if (!playerBox.HasValue)
                return;

            var observedPlayer = this as ArenaObservedPlayer;
            var showADS = espTypeSettings.ShowADS && IsAiming;
            var showDist = espTypeSettings.ShowDistance;
            var showHealth = espTypeSettings.ShowHealth;
            var showName = espTypeSettings.ShowName;
            var showKD = espTypeSettings.ShowKD;
            var showWep = espTypeSettings.ShowWeapon && observedPlayer?.Hands?.CurrentItem != null;
            var showAmmo = espTypeSettings.ShowAmmoType && observedPlayer?.Hands?.CurrentAmmo != null;
            var showBomb = espTypeSettings.ShowBomb && observedPlayer?.Gear?.HasBomb == true;
            var headPosition = new SKPoint(playerBox.Value.MidX, playerBox.Value.Top);

            if (renderMode is ESPPlayerRenderMode.Bones)
            {
                if (!this.Skeleton.UpdateESPBuffer())
                    return;

                canvas.DrawPoints(SKPointMode.Lines, Skeleton.ESPBuffer, espPaints.Item1);
            }
            else if (renderMode is ESPPlayerRenderMode.Box)
            {
                canvas.DrawRect(playerBox.Value, espPaints.Item1);

                baseScrPos.X = playerBox.Value.MidX;
                baseScrPos.Y = playerBox.Value.Bottom;
            }
            else if (renderMode is ESPPlayerRenderMode.HeadDot)
            {
                if (CameraManagerBase.WorldToScreen(ref Skeleton.Bones[Bones.HumanHead].Position, out var actualHeadPos, true, true))
                {
                    canvas.DrawCircle(actualHeadPos, 1.5f * ESP.Config.FontScale, espPaints.Item1);
                }
                else
                {
                    canvas.DrawCircle(headPosition, 1.5f * ESP.Config.FontScale, espPaints.Item1);
                }
            }

            if (BattleMode)
                return;

            var baseYOffset = 5f * ESP.Config.FontScale;
            var lineHeight = espPaints.Item2.TextSize * 1.2f * ESP.Config.FontScale;
            var currentY = headPosition.Y - baseYOffset;

            if (observedPlayer != null)
            {
                if (showADS)
                {
                    var adsPos = new SKPoint(headPosition.X, currentY);
                    canvas.DrawText("ADS", adsPos, espPaints.Item2);
                    currentY -= lineHeight;
                }

                if (LocalGameWorld.MatchMode == Enums.ERaidMode.BlastGang && showBomb)
                {
                    SKPoint bombPos = new SKPoint(headPosition.X, currentY);
                    canvas.DrawText("BOMB", bombPos, espPaints.Item2);
                    currentY -= lineHeight;
                }

                if (showHealth)
                    DrawHealthBar(canvas, observedPlayer, playerBox.Value);
            }

            if (showName)
            {
                var namePos = new SKPoint(headPosition.X, currentY);
                canvas.DrawText(Name, namePos, espPaints.Item2);
            }

            if (showDist || showWep || showAmmo || showKD)
            {
                var lines = new List<string>();

                if (showDist)
                    lines.Add($"{(int)dist}m");

                if (observedPlayer != null)
                {
                    string weaponAmmoText = null;

                    if (showWep && showAmmo)
                        weaponAmmoText = $"{observedPlayer.Hands.CurrentItem}/{observedPlayer.Hands.CurrentAmmo}";
                    else if (showWep)
                        weaponAmmoText = observedPlayer.Hands.CurrentItem;
                    else if (showAmmo)
                        weaponAmmoText = observedPlayer.Hands.CurrentAmmo;

                    if (weaponAmmoText != null)
                        lines.Add(weaponAmmoText);

                    if (showKD && observedPlayer != null && observedPlayer.Profile?.Overall_KD is float kd)
                        if (kd >= espTypeSettings.MinKD)
                            lines.Add(kd.ToString("n2"));
                }

                if (lines.Any())
                {
                    var textPt = new SKPoint(playerBox.Value.MidX, playerBox.Value.Bottom + espPaints.Item2.TextSize * ESP.Config.FontScale);
                    textPt.DrawESPText(canvas, this, localPlayer, false, espPaints.Item2, lines.ToArray());
                }
            }

            if (ESP.Config.ShowAimLock && IsAimbotLocked)
            {
                var info = MemWriteFeature<Aimbot>.Instance.Cache;
                if (info is not null &&
                    info.LastFireportPos is Vector3 fpPos &&
                    info.LastPlayerPos is Vector3 playerPos)
                {
                    if (!CameraManagerBase.WorldToScreen(ref fpPos, out var fpScreen))
                        return;
                    if (!CameraManagerBase.WorldToScreen(ref playerPos, out var playerScreen))
                        return;
                    canvas.DrawLine(fpScreen, playerScreen, SKPaints.PaintAimbotLockedLineESP);
                }
            }
        }

        /// <summary>
        /// Draws a health bar to the left of the player
        /// </summary>
        private void DrawHealthBar(SKCanvas canvas, ArenaObservedPlayer player, SKRect playerBounds)
        {
            var healthPercent = GetHealthPercentage(player);
            var healthColor = GetHealthColor(player.HealthStatus);
            var barWidth = 3f * ESP.Config.FontScale;
            var barHeight = playerBounds.Height; // Use full height of the player box
            var barOffsetX = 6f * ESP.Config.FontScale;

            var left = playerBounds.Left - barOffsetX - barWidth;
            var top = playerBounds.Top; // Align with the top of the player box

            var bgRect = new SKRect(left, top, left + barWidth, top + barHeight);

            canvas.DrawRect(bgRect, SKPaints.PaintESPHealthBarBg);

            var filledHeight = barHeight * healthPercent;
            var bottom = top + barHeight;
            var fillTop = bottom - filledHeight;
            var fillRect = new SKRect(left, fillTop, left + barWidth, bottom);

            var healthFillPaint = SKPaints.PaintESPHealthBar.Clone();
            healthFillPaint.Color = healthColor;

            canvas.DrawRect(fillRect, healthFillPaint);
            canvas.DrawRect(bgRect, SKPaints.PaintESPHealthBarBorder);
        }

        /// <summary>
        /// Gets health color based on player's health status
        /// </summary>
        private SKColor GetHealthColor(Enums.ETagStatus healthStatus)
        {
            return healthStatus switch
            {
                Enums.ETagStatus.Healthy => new SKColor(0, 255, 0),     // Green
                Enums.ETagStatus.Injured => new SKColor(255, 255, 0),   // Yellow
                Enums.ETagStatus.BadlyInjured => new SKColor(255, 165, 0), // Orange
                Enums.ETagStatus.Dying => new SKColor(255, 0, 0),       // Red
                _ => new SKColor(0, 255, 0)
            };
        }

        /// <summary>
        /// Gets health percentage based on observed player's health status
        /// This is a simplified approach - ideally would use actual health values if available
        /// </summary>
        private float GetHealthPercentage(ArenaObservedPlayer player)
        {
            return player.HealthStatus switch
            {
                Enums.ETagStatus.Healthy => 1.0f,
                Enums.ETagStatus.Injured => 0.75f,
                Enums.ETagStatus.BadlyInjured => 0.4f,
                Enums.ETagStatus.Dying => 0.15f,
                _ => 1.0f
            };
        }

        // <summary>
        // Gets Aimview drawing paintbrushes based on this Player Type.
        // </summary>
        private ValueTuple<SKPaint, SKPaint> GetESPPaints()
        {
            if (IsAimbotLocked)
                return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintAimbotLockedESP, SKPaints.TextAimbotLockedESP);

            if (IsFocused)
                return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintFocusedESP, SKPaints.TextFocusedESP);

            switch (Type)
            {
                case PlayerType.Teammate:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintFriendlyESP, SKPaints.TextFriendlyESP);
                case PlayerType.USEC:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintUSECESP, SKPaints.TextUSECESP);
                case PlayerType.BEAR:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintBEARESP, SKPaints.TextBEARESP);
                case PlayerType.AI:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintAIESP, SKPaints.TextAIESP);
                case PlayerType.SpecialPlayer:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintSpecialESP, SKPaints.TextSpecialESP);
                case PlayerType.Streamer:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintStreamerESP, SKPaints.TextStreamerESP);
                default:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintUSECESP, SKPaints.TextUSECESP);
            }
        }

        // <summary>
        // Gets mini radar paint brush based on this Player Type.
        // </summary>
        public SKPaint GetMiniRadarPaint()
        {
            if (IsAimbotLocked)
                return SKPaints.PaintMiniAimbotLocked;

            if (IsFocused)
                return SKPaints.PaintMiniFocused;

            if (this is LocalPlayer)
                return SKPaints.PaintMiniLocalPlayer;

            switch (Type)
            {
                case PlayerType.Teammate:
                    return SKPaints.PaintMiniTeammate;
                case PlayerType.USEC:
                    return SKPaints.PaintMiniUSEC;
                case PlayerType.BEAR:
                    return SKPaints.PaintMiniBEAR;
                case PlayerType.AI:
                    return SKPaints.PaintMiniAI;
                case PlayerType.SpecialPlayer:
                    return SKPaints.PaintMiniSpecial;
                case PlayerType.Streamer:
                    return SKPaints.PaintMiniStreamer;
                default:
                    return SKPaints.PaintMiniUSEC;
            }
        }

        /// <summary>
        /// Determine player type key for settings lookup
        /// </summary>
        public string DeterminePlayerTypeKey()
        {
            if (this is LocalPlayer)
                return "LocalPlayer";

            if (IsAimbotLocked)
                return "AimbotLocked";

            if (IsFocused)
                return "Focused";

            return Type.ToString();
        }

        #endregion

        #region Focused Players
        private static readonly HashSet<string> _focusedPlayers = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Toggles this player's focused status.
        /// Only applies to Human Controlled Players.
        /// </summary>
        public void ToggleFocus()
        {
            if (this is not ArenaObservedPlayer ||
                !this.IsHumanActive)
                return;
            string id = this.AccountID?.Trim();
            if (string.IsNullOrEmpty(id))
                return;
            lock (_focusedPlayers)
            {
                bool isFocused = _focusedPlayers.Contains(id);
                if (isFocused)
                    _focusedPlayers.Remove(id);
                else
                    _focusedPlayers.Add(id);
                IsFocused = !isFocused;
            }
        }

        /// <summary>
        /// Check if this Player was focused prior (from a different round,etc.)
        /// </summary>
        /// <returns>True if focused, otherwise False.</returns>
        protected bool CheckIfFocused()
        {
            string id = this.AccountID?.Trim();
            if (string.IsNullOrEmpty(id))
                return false;
            lock (_focusedPlayers)
            {
                return _focusedPlayers.Contains(id);
            }
        }
        #endregion

        #region Types

        /// <summary>
        /// Defines Player Unit Type (Player,PMC,Scav,etc.)
        /// </summary>
        public enum PlayerType
        {
            /// <summary>
            /// Default value if a type cannot be established.
            /// </summary>
            [Description("Default")]
            Default,
            /// <summary>
            /// Teammate of LocalPlayer.
            /// </summary>
            [Description("Teammate")]
            Teammate,
            /// <summary>
            /// Bot/AI Player.
            /// </summary>
            [Description("AI")]
            AI,
            /// <summary>
            /// Human Controlled USEC Player.
            /// </summary>
            [Description("USEC")]
            USEC,
            /// <summary>
            /// Human Controlled BEAR Player.
            /// </summary>
            [Description("BEAR")]
            BEAR,
            /// <summary>
            /// Human Controlled Hostile PMC/Scav that has a Twitch account name as their IGN.
            /// </summary>
            [Description("Streamer")]
            Streamer,
            /// <summary>
            /// 'Special' Human Controlled Hostile PMC/Scav (on the watchlist, or a special account type).
            /// </summary>
            [Description("Special Player")]
            SpecialPlayer
        }

        #endregion
    }
}
