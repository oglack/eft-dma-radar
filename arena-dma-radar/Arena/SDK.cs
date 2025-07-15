namespace SDK
{
    public readonly partial struct ClassNames
    {
        public readonly partial struct StreamerMode
        {
            public const uint ClassName_ClassToken = 0x2002159; // MDToken
            public const string ClassName = @"\uEAC1";
            public const string MethodName = @"IsLocalStreamer";
        }

        public readonly partial struct InertiaSettings
        {
            public const uint ClassName_ClassToken = 0x200183F; // MDToken
            public const string ClassName = @"\uE802";
        }

        public readonly partial struct GameSettings
        {
            public const uint ClassName_ClassToken = 0x20020F8; // MDToken
            public const string ClassName = @"\uEAB1";
        }

        public readonly partial struct AmmoTemplate
        {
            public const uint ClassName_ClassToken = 0x2003117; // MDToken
            public const uint MethodName_MethodToken = 0x6012A73; // MDToken
            public const string ClassName = @"\uF036";
            public const string MethodName = @"get_LoadUnloadModifier";
        }

        public readonly partial struct NoMalfunctions
        {
            public const uint ClassName_ClassToken = 0x2001A4D; // MDToken
            public const uint GetMalfunctionState_MethodToken = 0x600A57E; // MDToken
            public const string ClassName = @"EFT.Player+FirearmController";
            public const string GetMalfunctionState = @"GetMalfunctionState";
        }

        public readonly partial struct OpticCameraManagerContainer
        {
            public const uint ClassName_ClassToken = 0x200340B; // MDToken
            public const string ClassName = @"\uF1F5";
        }

        public readonly partial struct ScreenManager
        {
            public const uint ClassName_ClassToken = 0x2003A7E; // MDToken
            public const string ClassName = @"\uF28E";
        }

        public readonly partial struct FirearmController
        {
            public const uint ClassName_ClassToken = 0x2001A4D; // MDToken
            public const string ClassName = @"EFT.Player+FirearmController";
        }

        public readonly partial struct GridItemView
        {
            public const uint MethodName_MethodToken = 0x60163CC; // MDToken
            public const string MethodName = @"\uE011";
        }

        public readonly partial struct ProceduralWeaponAnimation
        {
            public const uint ClassName_ClassToken = 0x2002729; // MDToken
            public const uint MethodName_MethodToken = 0x600F2F4; // MDToken
            public const string ClassName = @"EFT.Animations.ProceduralWeaponAnimation";
            public const string MethodName = @"get_ShotNeedsFovAdjustments";
        }

        public readonly partial struct MovementContext
        {
            public const uint ClassName_ClassToken = 0x2001C65; // MDToken
            public const uint MethodName_MethodToken = 0x600B534; // MDToken
            public const string ClassName = @"EFT.MovementContext";
            public const string MethodName = @"SetPhysicalCondition";
        }

        public readonly partial struct GrenadeFlashScreenEffect
        {
            public const uint ClassName_ClassToken = 0x2000C73; // MDToken
            public const uint MethodName_MethodToken = 0x6005303; // MDToken
            public const string ClassName = @"GrenadeFlashScreenEffect";
            public const string MethodName = @"Update";
        }

        public readonly partial struct FovChanger
        {
            public const uint ClassName_ClassToken = 0x200340B; // MDToken
            public const uint MethodName_MethodToken = 0x601386B; // MDToken
            public const string ClassName = @"\uF1F5";
            public const string MethodName = @"SetFov";
        }

        public readonly partial struct VitalParts
        {
            public const uint ClassName_ClassToken = 0x20030BC; // MDToken
            public const uint MethodName_MethodToken = 0x601282A; // MDToken
            public const string ClassName = @"EFT.InventoryLogic.CompoundItem";
            public const string MethodName = @"\uE007";
        }

        public readonly partial struct InventoryLogic_Mod
        {
            public const uint ClassName_ClassToken = 0x2003133; // MDToken
            public const uint MethodName_MethodToken = 0x6012B0D; // MDToken
            public const string ClassName = @"EFT.InventoryLogic.Mod";
            public const string MethodName = @"get_RaidModdable";
        }
    }

    public readonly partial struct Offsets
    {
        public readonly partial struct TarkovApplication
        {
            public const uint GameOperationSubclass = 0xF0; // -.\uEA3A
        }

        public readonly partial struct GameWorld
        {
            public const uint Location = 0x90; // String
        }

        public readonly partial struct ClientLocalGameWorld
        {
            public const uint LocationId = 0x90; // String
            public const uint LootList = 0x118; // System.Collections.Generic.List<\uE314>
            public const uint RegisteredPlayers = 0x140; // System.Collections.Generic.List<IPlayer>
            public const uint MainPlayer = 0x1B0; // EFT.Player
            public const uint Grenades = 0x210; // -.\uE3E3<Int32, Throwable>
            public const uint IsInRaid = 0x290; // [HUMAN] Bool
            public const uint LoadBundlesAndCreatePools = 0x290; // Boolean
        }

        public readonly partial struct WorldController
        {
            public const uint Interactables = 0x30; // EFT.Interactive.WorldInteractiveObject[]
        }

        public readonly partial struct Interactable
        {
            public const uint KeyId = 0x58; // String
            public const uint Id = 0x60; // String
            public const uint _doorState = 0x118; // System.Byte
        }

        public readonly partial struct LevelSettings
        {
            public const uint AmbientMode = 0x70; // System.Int32
            public const uint EquatorColor = 0x84; // UnityEngine.Color
            public const uint GroundColor = 0x94; // UnityEngine.Color
        }

        public readonly partial struct EFTHardSettings
        {
            public const uint DecelerationSpeed = 0x1A0; // Single
            public const uint AIR_CONTROL_SAME_DIR = 0x248; // Single
            public const uint AIR_CONTROL_NONE_OR_ORT_DIR = 0x250; // Single
            public const uint LOOT_RAYCAST_DISTANCE = 0x26C; // Single
            public const uint DOOR_RAYCAST_DISTANCE = 0x274; // Single
            public const uint STOP_AIMING_AT = 0x2C8; // Single
            public const uint WEAPON_OCCLUSION_LAYERS = 0x2E8; // UnityEngine.LayerMask
            public const uint MOUSE_LOOK_HORIZONTAL_LIMIT = 0x3BC; // UnityEngine.Vector2
            public const uint MOUSE_LOOK_VERTICAL_LIMIT = 0x3C4; // UnityEngine.Vector2
            public const uint POSE_CHANGING_SPEED = 0x3F0; // Single
            public const uint MED_EFFECT_USING_PANEL = 0x40C; // Boolean
        }

        public readonly partial struct GlobalConfigs
        {
            public const uint Inertia = 0xE0; // -.\uE802.InertiaSettings
        }

        public readonly partial struct InertiaSettings
        {
            public const uint FallThreshold = 0x20; // Single
            public const uint BaseJumpPenaltyDuration = 0x4C; // Single
            public const uint BaseJumpPenalty = 0x54; // Single
            public const uint MoveTimeRange = 0xF4; // UnityEngine.Vector2
        }

        public readonly partial struct InventoryBlur
        {
            public const uint _upsampleTexDimension = 0x34; // System.Int32
            public const uint _blurCount = 0x3C; // Int32
            public const uint enabled = 0x44; // Boolean
        }

        public readonly partial struct NightVision
        {
            public const uint _on = 0xF4; // Boolean
        }

        public readonly partial struct ThermalVision
        {
            public const uint Material = 0x98; // UnityEngine.Material
            public const uint On = 0xE8; // Boolean
            public const uint IsNoisy = 0xE9; // Boolean
            public const uint IsFpsStuck = 0xEA; // Boolean
            public const uint IsMotionBlurred = 0xEB; // Boolean
            public const uint IsGlitch = 0xEC; // Boolean
            public const uint IsPixelated = 0xED; // Boolean
            public const uint ChromaticAberrationThermalShift = 0xF0; // Single
            public const uint UnsharpRadiusBlur = 0xF4; // Single
            public const uint UnsharpBias = 0xF8; // Single
        }

        public readonly partial struct Grenade
        {
            public const uint IsDestroyed = 0x5D; // Boolean
            public const uint WeaponSource = 0x80; // -.\uF09A
            public const uint Renderers = 0xB8; // UnityEngine.Renderer[]
        }

        public readonly partial struct Player
        {
            public const uint _characterController = 0x40; // -.ICharacterController
            public const uint MovementContext = 0x58; // EFT.MovementContext
            public const uint _playerBody = 0xC0; // EFT.PlayerBody
            public const uint ProceduralWeaponAnimation = 0x1E8; // EFT.Animations.ProceduralWeaponAnimation
            public const uint _animators = 0x3B8; // -.IAnimator[]
            public const uint Corpse = 0x3F0; // EFT.Interactive.Corpse
            public const uint Profile = 0x628; // EFT.Profile
            public const uint Physical = 0x640; // -.\uE3A3
            public const uint _inventoryController = 0x688; // -.Player.PlayerInventoryController
            public const uint _handsController = 0x690; // -.Player.AbstractHandsController
            public const uint EnabledAnimators = 0x9DC; // System.Int32
        }

        public readonly partial struct ObservedPlayerView
        {
            public const uint NickName = 0x50; // String
            public const uint AccountId = 0x58; // String
            public const uint PlayerBody = 0x68; // EFT.PlayerBody
            public const uint ObservedPlayerController = 0x88; // -.\uED4A
            public const uint Side = 0x108; // System.Int32
            public const uint IsAI = 0x119; // Boolean
            public const uint VisibleToCameraType = 0x120; // System.Int32
        }

        public readonly partial struct ObservedPlayerController
        {
            public static readonly uint[] MovementController = new uint[] { 0x100, 0x10 }; // -.\uED6D, -.\uED6F
            public const uint HandsController = 0x110; // -.\uED58
            public const uint HealthController = 0x128; // -.\uE455
            public const uint InventoryController = 0x150; // -.\uED40
        }

        public readonly partial struct ObservedMovementController
        {
            public const uint Rotation = 0x88; // UnityEngine.Vector2
            public const uint Velocity = 0x120; // UnityEngine.Vector3
        }

        public readonly partial struct ObservedHandsController
        {
            public const uint ItemInHands = 0x58; // EFT.InventoryLogic.Item
            public const uint BundleAnimationBones = 0xA0; // -.\uED42
        }

        public readonly partial struct BundleAnimationBonesController
        {
            public const uint ProceduralWeaponAnimationObs = 0xB8; // EFT.Animations.ProceduralWeaponAnimation
        }

        public readonly partial struct ProceduralWeaponAnimationObs
        {
            public const uint _isAimingObs = 0x1DD; // Boolean
        }

        public readonly partial struct ObservedHealthController
        {
            public const uint Player = 0x10; // EFT.NextObservedPlayer.ObservedPlayerView
            public const uint PlayerCorpse = 0x18; // EFT.Interactive.ObservedCorpse
            public const uint HealthStatus = 0xE0; // System.Int32
        }

        public readonly partial struct SimpleCharacterController
        {
            public const uint _collisionMask = 0x60; // UnityEngine.LayerMask
            public const uint _speedLimit = 0x7C; // Single
            public const uint _sqrSpeedLimit = 0x80; // Single
            public const uint velocity = 0xEC; // UnityEngine.Vector3
        }

        public readonly partial struct Physical
        {
            public const uint Stamina = 0x38; // -.\uE3A2
            public const uint HandsStamina = 0x40; // -.\uE3A2
            public const uint Oxygen = 0x48; // -.\uE3A2
            public const uint Overweight = 0x8C; // Single
            public const uint WalkOverweight = 0x90; // Single
            public const uint WalkSpeedLimit = 0x94; // Single
            public const uint Inertia = 0x98; // Single
            public const uint WalkOverweightLimits = 0xD8; // UnityEngine.Vector2
            public const uint BaseOverweightLimits = 0xE0; // UnityEngine.Vector2
            public const uint SprintOverweightLimits = 0xF4; // UnityEngine.Vector2
            public const uint SprintWeightFactor = 0x104; // Single
            public const uint SprintAcceleration = 0x114; // Single
            public const uint PreSprintAcceleration = 0x118; // Single
            public const uint IsOverweightA = 0x11C; // Boolean
            public const uint IsOverweightB = 0x11D; // Boolean
        }

        public readonly partial struct PhysicalValue
        {
            public const uint Current = 0x48; // Single
        }

        public readonly partial struct ProceduralWeaponAnimation
        {
            public const uint HandsContainer = 0x20; // EFT.Animations.PlayerSpring
            public const uint Breath = 0x30; // EFT.Animations.BreathEffector
            public const uint MotionReact = 0x40; // -.MotionEffector
            public const uint Shootingg = 0x50; // -.ShotEffector
            public const uint _optics = 0xC8; // System.Collections.Generic.List<SightNBone>
            public const uint Mask = 0x158; // System.Int32
            public const uint _isAiming = 0x1DD; // Boolean
            public const uint _aimingSpeed = 0x1FC; // Single
            public const uint _fovCompensatoryDistance = 0x210; // Single
            public const uint _compensatoryScale = 0x240; // Single
            public const uint _shotDirection = 0x244; // UnityEngine.Vector3
            public const uint CameraSmoothOut = 0x280; // Single
            public const uint PositionZeroSum = 0x35C; // UnityEngine.Vector3
            public const uint ShotNeedsFovAdjustments = 0x427; // Boolean
        }

        public readonly partial struct HandsContainer
        {
            public const uint CameraOffset = 0xE4; // UnityEngine.Vector3
        }

        public readonly partial struct SightNBone
        {
            public const uint Mod = 0x10; // EFT.InventoryLogic.SightComponent
        }

        public readonly partial struct MotionEffector
        {
            public const uint _mouseProcessors = 0x18; // -.\uE44A[]
            public const uint _movementProcessors = 0x20; // -.\uE449[]
        }

        public readonly partial struct BreathEffector
        {
            public const uint Intensity = 0xA4; // Single
        }

        public readonly partial struct ShotEffector
        {
            public const uint NewShotRecoil = 0x18; // EFT.Animations.NewRecoil.NewRecoilShotEffect
        }

        public readonly partial struct NewShotRecoil
        {
            public const uint IntensitySeparateFactors = 0x94; // UnityEngine.Vector3
        }

        public readonly partial struct VisorEffect
        {
            public const uint Intensity = 0xC8; // Single
        }

        public readonly partial struct Profile
        {
            public const uint Id = 0x10; // String
            public const uint AccountId = 0x18; // String
            public const uint Info = 0x40; // -.\uE875
        }

        public readonly partial struct PlayerInfo
        {
            public const uint Nickname = 0x20; // String
            public const uint Settings = 0x60; // -.\uE9B8
            public const uint Side = 0xA8; // [HUMAN] Int32
            public const uint RegistrationDate = 0xAC; // Int32
            public const uint MemberCategory = 0xB8; // System.Int32
            public const uint Experience = 0xBC; // Int32
        }

        public readonly partial struct PlayerInfoSettings
        {
            public const uint Role = 0x10; // System.Int32
        }

        public readonly partial struct SkillManager
        {
            public const uint StrengthBuffJumpHeightInc = 0x60; // -.SkillManager.FloatBuff
            public const uint StrengthBuffThrowDistanceInc = 0x70; // -.SkillManager.FloatBuff
            public const uint MagDrillsLoadSpeed = 0x180; // -.SkillManager.FloatBuff
            public const uint MagDrillsUnloadSpeed = 0x188; // -.SkillManager.FloatBuff
        }

        public readonly partial struct SkillValueContainer
        {
            public const uint Value = 0x30; // Single
        }

        public readonly partial struct ItemHandsController
        {
            public const uint Item = 0x68; // EFT.InventoryLogic.Item
        }

        public readonly partial struct FirearmController
        {
            public const uint Fireport = 0xD0; // EFT.BifacialTransform
            public const uint TotalCenterOfImpact = 0x1A0; // Single
        }

        public readonly partial struct ClientFirearmController
        {
            public const uint WeaponLn = 0x18C; // Single
            public const uint ShotIndex = 0x420; // SByte
        }

        public readonly partial struct MovementContext
        {
            public const uint Player = 0x10; // EFT.Player
            public const uint CurrentState = 0xE0; // EFT.BaseMovementState
            public const uint _states = 0x1E0; // System.Collections.Generic.Dictionary<Byte, BaseMovementState>
            public const uint _movementStates = 0x200; // -.IPlayerStateContainerBehaviour[]
            public const uint _tilt = 0x268; // Single
            public const uint _rotation = 0x27C; // UnityEngine.Vector2
            public const uint _physicalCondition = 0x300; // System.Int32
            public const uint _speedLimitIsDirty = 0x305; // Boolean
            public const uint StateSpeedLimit = 0x308; // Single
            public const uint StateSprintSpeedLimit = 0x30C; // Single
            public const uint _lookDirection = 0x424; // UnityEngine.Vector3
            public const uint WalkInertia = 0x4B0; // Single
            public const uint SprintBrakeInertia = 0x4B4; // Single
        }

        public readonly partial struct MovementState
        {
            public const uint Name = 0x21; // System.Byte
            public const uint AnimatorStateHash = 0x24; // Int32
            public const uint StickToGround = 0x5C; // Boolean
        }

        public readonly partial struct InventoryController
        {
            public const uint Inventory = 0x130; // EFT.InventoryLogic.Inventory
        }

        public readonly partial struct Inventory
        {
            public const uint Equipment = 0x10; // EFT.InventoryLogic.InventoryEquipment
        }

        public readonly partial struct Equipment
        {
            public const uint Grids = 0x90; // -.\uEF89[]
            public const uint Slots = 0x98; // EFT.InventoryLogic.Slot[]
        }

        public readonly partial struct Grids
        {
            public const uint ContainedItems = 0x48; // -.\uEF8B
        }

        public readonly partial struct GridContainedItems
        {
            public const uint Items = 0x18; // System.Collections.Generic.List<Item>
        }

        public readonly partial struct Slot
        {
            public const uint ContainedItem = 0x48; // EFT.InventoryLogic.Item
            public const uint ID = 0x58; // String
        }

        public readonly partial struct InteractiveLootItem
        {
            public const uint Item = 0xB8; // EFT.InventoryLogic.Item
        }

        public readonly partial struct InteractiveCorpse
        {
            public const uint PlayerBody = 0x138; // EFT.PlayerBody
        }

        public readonly partial struct DizSkinningSkeleton
        {
            public const uint _values = 0x30; // System.Collections.Generic.List<Transform>
        }

        public readonly partial struct LootableContainer
        {
            public const uint InteractingPlayer = 0xC0; // EFT.IPlayer
            public const uint ItemOwner = 0x130; // -.\uEF65
            public const uint Template = 0x138; // String
        }

        public readonly partial struct LootableContainerItemOwner
        {
            public const uint RootItem = 0xD0; // EFT.InventoryLogic.Item
        }

        public readonly partial struct LootItem
        {
            public const uint Template = 0x58; // EFT.InventoryLogic.ItemTemplate
            public const uint StackObjectsCount = 0x7C; // Int32
            public const uint Version = 0x80; // Int32
        }

        public readonly partial struct LootItemMod
        {
            public const uint Grids = 0x90; // -.\uEF89[]
            public const uint Slots = 0x98; // EFT.InventoryLogic.Slot[]
        }

        public readonly partial struct LootItemModGrids
        {
            public const uint ItemCollection = 0x48; // -.\uEF8B
        }

        public readonly partial struct LootItemModGridsItemCollection
        {
            public const uint List = 0x18; // System.Collections.Generic.List<Item>
        }

        public readonly partial struct LootItemWeapon
        {
            public const uint FireMode = 0xB8; // EFT.InventoryLogic.FireModeComponent
            public const uint Chambers = 0xD0; // EFT.InventoryLogic.Slot[]
            public const uint _magSlotCache = 0xF0; // EFT.InventoryLogic.Slot
        }

        public readonly partial struct FireModeComponent
        {
            public const uint FireMode = 0x28; // System.Byte
        }

        public readonly partial struct LootItemMagazine
        {
            public const uint Cartridges = 0xC0; // EFT.InventoryLogic.StackSlot
            public const uint LoadUnloadModifier = 0x1C4; // Single
        }

        public readonly partial struct MagazineClass
        {
            public const uint StackObjectsCount = 0x7C; // Int32
        }

        public readonly partial struct StackSlot
        {
            public const uint _items = 0x28; // System.Collections.Generic.List<Item>
            public const uint MaxCount = 0x50; // Int32
        }

        public readonly partial struct ItemTemplate
        {
            public const uint ShortName = 0x18; // String
            public const uint _id = 0x68; // EFT.MongoID
        }

        public readonly partial struct ModTemplate
        {
            public const uint Velocity = 0x190; // Single
        }

        public readonly partial struct AmmoTemplate
        {
            public const uint InitialSpeed = 0x1F0; // Single
            public const uint BallisticCoeficient = 0x204; // Single
            public const uint BulletMassGram = 0x28C; // Single
            public const uint BulletDiameterMilimeters = 0x290; // Single
        }

        public readonly partial struct WeaponTemplate
        {
            public const uint Velocity = 0x27C; // Single
        }

        public readonly partial struct PlayerBody
        {
            public const uint SkeletonRootJoint = 0x30; // Diz.Skinning.Skeleton
            public const uint BodySkins = 0x48; // System.Collections.Generic.Dictionary<Int32, LoddedSkin>
            public const uint _bodyRenderers = 0x58; // -.\uE454[]
            public const uint SlotViews = 0x78; // -.\uE3E3<Int32, \uE001>
            public const uint PointOfView = 0xA0; // -.\uE719<Int32>
        }

        public readonly partial struct PointOfView
        {
            public const uint POV = 0x10; // Var
        }

        public readonly partial struct PlayerSpring
        {
            public const uint CameraTransform = 0x70; // UnityEngine.Transform
        }

        public readonly partial struct PlayerBodySubclass
        {
            public const uint Dresses = 0x40; // EFT.Visual.Dress[]
        }

        public readonly partial struct Dress
        {
            public const uint Renderers = 0x30; // UnityEngine.Renderer[]
        }

        public readonly partial struct Skeleton
        {
            public const uint _values = 0x30; // System.Collections.Generic.List<Transform>
        }

        public readonly partial struct LoddedSkin
        {
            public const uint _lods = 0x20; // Diz.Skinning.AbstractSkin[]
        }

        public readonly partial struct Skin
        {
            public const uint _skinnedMeshRenderer = 0x28; // UnityEngine.SkinnedMeshRenderer
        }

        public readonly partial struct TorsoSkin
        {
            public const uint _skin = 0x28; // Diz.Skinning.Skin
        }

        public readonly partial struct SlotViewsContainer
        {
            public const uint Dict = 0x10; // System.Collections.Generic.Dictionary<Var, Var>
        }

        public readonly partial struct WeatherController
        {
            public const uint WeatherDebug = 0x68; // EFT.Weather.WeatherDebug
        }

        public readonly partial struct WeatherDebug
        {
            public const uint isEnabled = 0x18; // Boolean
            public const uint WindMagnitude = 0x1C; // Single
            public const uint CloudDensity = 0x2C; // Single
            public const uint Fog = 0x30; // Single
            public const uint Rain = 0x34; // Single
            public const uint LightningThunderProbability = 0x38; // Single
        }

        public readonly partial struct TOD_Scattering
        {
            public const uint sky = 0x20; // -.TOD_Sky
        }

        public readonly partial struct TOD_Sky
        {
            public const uint Cycle = 0x20; // -.TOD_CycleParameters
            public const uint TOD_Components = 0x80; // -.TOD_Components
        }

        public readonly partial struct TOD_CycleParameters
        {
            public const uint Hour = 0x10; // Single
        }

        public readonly partial struct TOD_Components
        {
            public const uint TOD_Time = 0x118; // -.TOD_Time
        }

        public readonly partial struct TOD_Time
        {
            public const uint LockCurrentTime = 0x70; // Boolean
        }

        public readonly partial struct PrismEffects
        {
            public const uint useVignette = 0x12C; // Boolean
            public const uint useExposure = 0x1C0; // Boolean
        }

        public readonly partial struct CC_Vintage
        {
            public const uint amount = 0x40; // Single
        }

        public readonly partial struct GPUInstancerManager
        {
            public const uint runtimeDataList = 0x48; // System.Collections.Generic.List<\uE5D8>
        }

        public readonly partial struct RuntimeDataList
        {
            public const uint instanceBounds = 0x68; // UnityEngine.Bounds
        }

        public readonly partial struct GameSettingsContainer
        {
            public const uint Game = 0x10; // -.\uEAB1.\uE000<\uEAC2, \uEAC1>
            public const uint Graphics = 0x28; // -.\uEAB1.\uE000<\uEABE, \uEABC>
        }

        public readonly partial struct GameSettingsInnerContainer
        {
            public const uint Settings = 0x10; // Var
            public const uint Controller = 0x30; // Var
        }

        public readonly partial struct GameSettings
        {
            public const uint FieldOfView = 0x60; // Bsg.GameSettings.GameSetting<Int32>
            public const uint HeadBobbing = 0x68; // Bsg.GameSettings.GameSetting<Single>
            public const uint AutoEmptyWorkingSet = 0x70; // Bsg.GameSettings.GameSetting<Boolean>
        }

        public readonly partial struct GraphicsSettings
        {
            public const uint DisplaySettings = 0x20; // Bsg.GameSettings.GameSetting<\uEAB7>
        }

        public readonly partial struct NetworkContainer
        {
            public const uint NextRequestIndex = 0x8; // Int64
            public const uint PhpSessionId = 0x30; // String
            public const uint AppVersion = 0x38; // String
        }

        public readonly partial struct ScreenManager
        {
            public const uint Instance = 0x0; // -.\uF28E
            public const uint CurrentScreenController = 0x28; // -.\uF290<Var>
        }

        public readonly partial struct CurrentScreenController
        {
            public const uint Generic = 0x20; // Var
        }

        public readonly partial struct BSGGameSetting
        {
            public const uint ValueClass = 0x28; // [HUMAN] ulong
        }

        public readonly partial struct BSGGameSettingValueClass
        {
            public const uint Value = 0x30; // [HUMAN] T
        }

        public readonly partial struct SSAA
        {
            public const uint OpticMaskMaterial = 0x58; // [HUMAN] UnityEngine.Material
        }

        public readonly partial struct BloomAndFlares
        {
            public const uint BloomIntensity = 0xB8; // [HUMAN] Single
        }

        public readonly partial struct OpticCameraManagerContainer
        {
            public const uint Instance = 0x0; // -.\uF1F5
            public const uint OpticCameraManager = 0x10; // -.\uF1F9
            public const uint FPSCamera = 0x68; // UnityEngine.Camera
        }

        public readonly partial struct OpticCameraManager
        {
            public const uint Camera = 0x68; // UnityEngine.Camera
            public const uint CurrentOpticSight = 0x70; // EFT.CameraControl.OpticSight
        }

        public readonly partial struct OpticSight
        {
            public const uint LensRenderer = 0x20; // UnityEngine.Renderer
        }

        public readonly partial struct SightComponent
        {
            public const uint _template = 0x20; // -.\uEF81
            public const uint ScopesSelectedModes = 0x30; // System.Int32[]
            public const uint SelectedScope = 0x38; // Int32
            public const uint ScopeZoomValue = 0x3C; // Single
        }

        public readonly partial struct SightInterface
        {
            public const uint Zooms = 0x1B8; // System.Single[]
        }

        public readonly partial struct NetworkGame
        {
            public const uint NetworkGameData = 0x70; // -.\uE9B7
        }

        public readonly partial struct NetworkGameData
        {
            public const uint raidMode = 0x4C; // System.Int32
        }
    }

    public readonly partial struct Enums
    {
        public enum EPlayerState
        {
            None = 0,
            Idle = 1,
            ProneIdle = 2,
            ProneMove = 3,
            Run = 4,
            Sprint = 5,
            Jump = 6,
            FallDown = 7,
            Transition = 8,
            BreachDoor = 9,
            Loot = 10,
            Pickup = 11,
            Open = 12,
            Close = 13,
            Unlock = 14,
            Sidestep = 15,
            DoorInteraction = 16,
            Approach = 17,
            Prone2Stand = 18,
            Transit2Prone = 19,
            Plant = 20,
            Stationary = 21,
            Roll = 22,
            JumpLanding = 23,
            ClimbOver = 24,
            ClimbUp = 25,
            VaultingFallDown = 26,
            VaultingLanding = 27,
            BlindFire = 28,
            IdleWeaponMounting = 29,
            IdleZombieState = 30,
            MoveZombieState = 31,
            TurnZombieState = 32,
            StartMoveZombieState = 33,
            EndMoveZombieState = 34,
            DoorInteractionZombieState = 35,
        }

        [Flags]
        public enum EMemberCategory
        {
            Default = 0,
            Developer = 1,
            UniqueId = 2,
            Trader = 4,
            Group = 8,
            System = 16,
            ChatModerator = 32,
            ChatModeratorWithPermanentBan = 64,
            UnitTest = 128,
            Sherpa = 256,
            Emissary = 512,
            Unheard = 1024,
        }

        public enum ERaidMode
        {
            Online = 0,
            Local = 1,
            Coop = 2,
            OverRun = 3,
            TeamFight = 4,
            LastHero = 5,
            FinalRun = 6,
            OneManArmy = 7,
            Duel = 8,
            ShootOut = 9,
            ShootOutSolo = 10,
            ShootOutDuo = 11,
            ShootOutTrio = 12,
            BlastGang = 13,
            CheckPoint = 14,
        }

        public enum EMalfunctionState
        {
            None = 0,
            Misfire = 1,
            Jam = 2,
            HardSlide = 3,
            SoftSlide = 4,
            Feed = 5,
        }

        [Flags]
        public enum EPhysicalCondition
        {
            None = 0,
            OnPainkillers = 1,
            LeftLegDamaged = 2,
            RightLegDamaged = 4,
            ProneDisabled = 8,
            LeftArmDamaged = 16,
            RightArmDamaged = 32,
            Tremor = 64,
            UsingMeds = 128,
            HealingLegs = 256,
            JumpDisabled = 512,
            SprintDisabled = 1024,
            ProneMovementDisabled = 2048,
            Panic = 4096,
        }

        [Flags]
        public enum EProceduralAnimationMask
        {
            Breathing = 1,
            Walking = 2,
            MotionReaction = 4,
            ForceReaction = 8,
            Shooting = 16,
            DrawDown = 32,
            Aiming = 64,
            HandShake = 128,
        }

        public enum EFireMode
        {
            fullauto = 0,
            single = 1,
            doublet = 2,
            burst = 3,
            doubleaction = 4,
            semiauto = 5,
            grenadeThrowing = 6,
            greanadePlanting = 7,
        }

        public enum ArmbandColorType
        {
            red = 1,
            fuchsia = 2,
            yellow = 3,
            green = 4,
            azure = 5,
            white = 6,
            blue = 7,
            grey = 8,
        }

        public enum ECameraType
        {
            Default = 0,
            Spectator = 1,
            UIBackground = 2,
            KillCamera = 3,
        }
    }
}
