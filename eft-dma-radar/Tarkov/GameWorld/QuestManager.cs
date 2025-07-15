using eft_dma_radar.Tarkov.EFTPlayer;
using System.Collections.Frozen;
using eft_dma_shared.Common.Unity;
using eft_dma_shared.Common.Unity.Collections;
using eft_dma_shared.Common.Maps;
using eft_dma_shared.Common.Players;
using eft_dma_shared.Common.ESP;
using eft_dma_shared.Common.Misc.Data;
using eft_dma_shared.Common.Misc;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.UI.ESP;
using eft_dma_radar.UI.Pages;
using eft_dma_radar;
using System.Diagnostics;

namespace eft_dma_radar.Tarkov.GameWorld
{
    public sealed class QuestManager
    {
        private static Config Config => Program.Config;

        private static readonly FrozenDictionary<string, string> _mapToId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "factory4_day", "55f2d3fd4bdc2d5f408b4567" },
            { "factory4_night", "59fc81d786f774390775787e" },
            { "bigmap", "56f40101d2720b2a4d8b45d6" },
            { "woods", "5704e3c2d2720bac5b8b4567" },
            { "lighthouse", "5704e4dad2720bb55b8b4567" },
            { "shoreline", "5704e554d2720bac5b8b456e" },
            { "labyrinth", "6733700029c367a3d40b02af" },
            { "rezervbase", "5704e5fad2720bc05b8b4567" },
            { "interchange", "5714dbc024597771384a510d" },
            { "tarkovstreets", "5714dc692459777137212e12" },
            { "laboratory", "5b0fc42d86f7744a585f9105" },
            { "Sandbox", "653e6760052c01c1c805532f" },
            { "Sandbox_high", "65b8d6f5cdde2479cb2a3125" }
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        private static FrozenDictionary<string, FrozenDictionary<string, Vector3>> _questZones;
        private static FrozenDictionary<string, FrozenDictionary<string, List<Vector3>>> _questOutlines;
        private static bool _lastKappaFilterState;
        private static bool _lastOptionalFilterState;

        static QuestManager()
        {
            UpdateCaches();
        }

        public static void UpdateCaches()
        {
            if (_lastKappaFilterState != Config.QuestHelper.KappaFilter ||
                _lastOptionalFilterState != Config.QuestHelper.OptionalTaskFilter ||
                _questZones == null || _questOutlines == null)
            {
                _questZones = GetQuestZones();
                _questOutlines = GetQuestOutlines();
                _lastKappaFilterState = Config.QuestHelper.KappaFilter;
                _lastOptionalFilterState = Config.QuestHelper.OptionalTaskFilter;
            }
        }

        public static EntityTypeSettings Settings => Config.EntityTypeSettings.GetSettings("QuestZone");
        public static EntityTypeSettingsESP ESPSettings => ESP.Config.EntityTypeESPSettings.GetSettings("QuestZone");

        private readonly Stopwatch _rateLimit = new();
        private readonly ulong _profile;

        public QuestManager(ulong profile)
        {
            _profile = profile;
            Refresh();
        }

        /// <summary>
        /// All currently active quests with their objectives and completion status.
        /// </summary>
        public IReadOnlyList<Quest> ActiveQuests { get; private set; } = new List<Quest>();

        /// <summary>
        /// Contains all item IDs that are required for incomplete quest objectives.
        /// </summary>
        public IReadOnlySet<string> RequiredItems { get; private set; } = new HashSet<string>();

        /// <summary>
        /// Contains all quest locations for the current map.
        /// </summary>
        public IReadOnlyList<QuestLocation> LocationConditions { get; private set; } = new List<QuestLocation>();

        /// <summary>
        /// All completed condition IDs across all active quests.
        /// </summary>
        public IReadOnlySet<string> AllCompletedConditions { get; private set; } = new HashSet<string>();

        /// <summary>
        /// Current Map ID.
        /// </summary>
        private static string MapID
        {
            get
            {
                var id = Memory.MapID;
                id ??= "MAPDEFAULT";
                return id;
            }
        }

        /// <summary>
        /// Checks if a specific item ID is required for any incomplete quest condition.
        /// </summary>
        /// <param name="itemId">The item's BSG ID</param>
        /// <returns>True if this item is required for an incomplete quest condition</returns>
        public bool IsItemRequired(string itemId)
        {
            return RequiredItems.Contains(itemId);
        }

        /// <summary>
        /// Gets all quests that require items on the current map.
        /// </summary>
        public IEnumerable<Quest> GetQuestsForCurrentMap()
        {
            if (!_mapToId.TryGetValue(MapID, out var currentMapId))
                return Enumerable.Empty<Quest>();

            return ActiveQuests.Where(quest =>
            {
                var hasLocationObjective = quest.Objectives.Any(obj =>
                    obj.LocationObjectives.Any(loc => loc.MapId == currentMapId) ||
                    obj.HasLocationRequirement
                );

                if (hasLocationObjective)
                    return true;

                if (EftDataManager.TaskData.TryGetValue(quest.Id, out var taskData) && taskData.Objectives != null)
                {
                    return taskData.Objectives.Any(objective =>
                        objective.Maps != null && objective.Maps.Any(map => map.Id == currentMapId)
                    );
                }

                return false;
            });
        }

        /// <summary>
        /// Gets all other active quests (not specific to current map).
        /// </summary>
        public IEnumerable<Quest> GetOtherQuests()
        {
            var questsForCurrentMap = GetQuestsForCurrentMap().Select(q => q.Id).ToHashSet();
            return ActiveQuests.Where(quest => !questsForCurrentMap.Contains(quest.Id));
        }

        public void Refresh()
        {
            UpdateCaches();

            if (_rateLimit.IsRunning && _rateLimit.Elapsed.TotalSeconds < 2d)
                return;

            var activeQuests = new List<Quest>();
            var allRequiredItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var allLocationConditions = new List<QuestLocation>();
            var allCompletedConditions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var questsData = Memory.ReadPtr(_profile + Offsets.Profile.QuestsData);
            using var questsDataList = MemList<ulong>.Get(questsData);

            foreach (var qDataEntry in questsDataList)
            {
                try
                {
                    var qStatus = Memory.ReadValue<int>(qDataEntry + Offsets.QuestData.Status);
                    if (qStatus != 2) // 2 == Started
                        continue;

                    var qIDPtr = Memory.ReadPtr(qDataEntry + Offsets.QuestData.Id);
                    var qID = Memory.ReadUnityString(qIDPtr);

                    if (Config.QuestHelper.BlacklistedQuests.Contains(qID, StringComparer.OrdinalIgnoreCase))
                        continue;

                    if (Config.QuestHelper.KappaFilter &&
                        EftDataManager.TaskData.TryGetValue(qID, out var taskElement) &&
                        !taskElement.KappaRequired)
                    {
                        continue;
                    }

                    var completedPtr = Memory.ReadPtr(qDataEntry + Offsets.QuestData.CompletedConditions);
                    using var completedHS = MemHashSet<Types.MongoID>.Get(completedPtr);
                    var questCompletedConditions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var c in completedHS)
                    {
                        var completedCond = Memory.ReadUnityString(c.Value.StringID);
                        questCompletedConditions.Add(completedCond);
                        allCompletedConditions.Add(completedCond);
                    }

                    var quest = CreateQuestFromGameData(qID, qDataEntry, questCompletedConditions);
                    if (quest != null)
                    {
                        activeQuests.Add(quest);

                        foreach (var item in quest.RequiredItems)
                        {
                            allRequiredItems.Add(item);
                        }

                        foreach (var objective in quest.Objectives)
                        {
                            allLocationConditions.AddRange(objective.LocationObjectives);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LoneLogging.WriteLine($"[QuestManager] ERROR parsing Quest at 0x{qDataEntry.ToString("X")}: {ex}");
                }
            }

            ActiveQuests = activeQuests;
            RequiredItems = allRequiredItems;
            LocationConditions = allLocationConditions;
            AllCompletedConditions = allCompletedConditions;

            if (MainWindow.Window?.GeneralSettingsControl?.QuestItems?.Count != ActiveQuests.Count)
                MainWindow.Window?.GeneralSettingsControl?.RefreshQuestHelper();

            _rateLimit.Restart();
        }

        private Quest CreateQuestFromGameData(string questId, ulong qDataEntry, HashSet<string> completedConditions)
        {
            try
            {
                if (!EftDataManager.TaskData.TryGetValue(questId, out var taskData))
                    return null;

                var quest = new Quest
                {
                    Id = questId,
                    Name = taskData.Name ?? "Unknown Quest",
                    KappaRequired = taskData.KappaRequired,
                    CompletedConditions = completedConditions,
                    Objectives = new List<QuestObjective>()
                };

                var qTemplate = Memory.ReadPtr(qDataEntry + Offsets.QuestData.Template);
                var qConditions = Memory.ReadPtr(qTemplate + Offsets.QuestTemplate.Conditions);

                using var qCondDict = MemDictionary<int, ulong>.Get(qConditions);

                foreach (var qDicCondEntry in qCondDict)
                {
                    var condListPtr = Memory.ReadPtr(qDicCondEntry.Value + Offsets.QuestConditionsContainer.ConditionsList);
                    using var condList = MemList<ulong>.Get(condListPtr);

                    foreach (var condition in condList)
                    {
                        ParseQuestCondition(condition, quest, completedConditions);
                    }
                }

                var requiredItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var objective in quest.Objectives)
                {
                    if (!objective.IsCompleted)
                    {
                        foreach (var itemId in objective.RequiredItemIds)
                        {
                            requiredItems.Add(itemId);
                        }
                    }
                }

                quest.RequiredItems = requiredItems;

                if (taskData.Objectives != null)
                {
                    for (int i = 0; i < taskData.Objectives.Count; i++)
                    {
                        var eftObj = taskData.Objectives[i];
                    }

                    for (int i = 0; i < quest.Objectives.Count; i++)
                    {
                        var parsedObj = quest.Objectives[i];
                    }

                    for (int i = 0; i < Math.Min(taskData.Objectives.Count, quest.Objectives.Count); i++)
                    {
                        var eftObjective = taskData.Objectives[i];
                        var parsedObjective = quest.Objectives[i];

                        parsedObjective.Optional = eftObjective.Optional;
                    }
                }

                return quest;
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[QuestManager] ERROR creating Quest for {questId}: {ex}");
                return null;
            }
        }

        private void ParseQuestCondition(ulong condition, Quest quest, HashSet<string> completedConditions)
        {
            try
            {
                var condIDPtr = Memory.ReadValue<Types.MongoID>(condition + Offsets.QuestCondition.id);
                var condID = Memory.ReadUnityString(condIDPtr.StringID);
                var condName = ObjectClass.ReadName(condition);

                var isCompleted = completedConditions.Contains(condID);
                var isOptional = false;

                if (EftDataManager.TaskData.TryGetValue(quest.Id, out var taskData) && taskData.Objectives != null)
                {
                    var matchingObjective = taskData.Objectives.FirstOrDefault(obj => obj.Id == condID);
                    isOptional = matchingObjective?.Optional ?? false;
                }

                if (condName == "ConditionFindItem" || condName == "ConditionHandoverItem")
                {
                    var targetArray = Memory.ReadPtr(condition + Offsets.QuestConditionFindItem.target);
                    using var targets = MemArray<ulong>.Get(targetArray);

                    var itemIds = new List<string>();
                    foreach (var targetPtr in targets)
                    {
                        var target = Memory.ReadUnityString(targetPtr);
                        itemIds.Add(target);
                    }

                    var objective = new QuestObjective
                    {
                        Id = condID,
                        Type = QuestObjectiveType.FindItem,
                        Optional = isOptional,
                        Description = $"{(condName == "ConditionFindItem" ? "Find" : "Hand over")} items",
                        IsCompleted = isCompleted,
                        RequiredItemIds = itemIds,
                        LocationObjectives = new List<QuestLocation>()
                    };

                    quest.Objectives.Add(objective);
                }
                else if (condName == "ConditionPlaceBeacon" || condName == "ConditionLeaveItemAtLocation")
                {
                    var zoneIDPtr = Memory.ReadPtr(condition + Offsets.QuestConditionPlaceBeacon.zoneId);
                    var target = Memory.ReadUnityString(zoneIDPtr);

                    var location = CreateQuestLocation(quest.Id, target);
                    if (location != null)
                    {
                        var objective = new QuestObjective
                        {
                            Id = condID,
                            Type = QuestObjectiveType.PlaceItem,
                            Optional = isOptional,
                            Description = $"Place item at {target}",
                            IsCompleted = isCompleted,
                            RequiredItemIds = new List<string>(),
                            LocationObjectives = new List<QuestLocation> { location }
                        };

                        quest.Objectives.Add(objective);
                    }
                }
                else if (condName == "ConditionVisitPlace")
                {
                    var targetPtr = Memory.ReadPtr(condition + Offsets.QuestConditionVisitPlace.target);
                    var target = Memory.ReadUnityString(targetPtr);

                    var location = CreateQuestLocation(quest.Id, target);
                    if (location != null)
                    {
                        var objective = new QuestObjective
                        {
                            Id = condID,
                            Type = QuestObjectiveType.VisitLocation,
                            Optional = isOptional,
                            Description = $"Visit {target}",
                            IsCompleted = isCompleted,
                            RequiredItemIds = new List<string>(),
                            LocationObjectives = new List<QuestLocation> { location }
                        };

                        quest.Objectives.Add(objective);
                    }
                }
                else if (condName == "ConditionCounterCreator")
                {
                    var conditionsPtr = Memory.ReadPtr(condition + Offsets.QuestConditionCounterCreator.Conditions);
                    var conditionsListPtr = Memory.ReadPtr(conditionsPtr + Offsets.QuestConditionsContainer.ConditionsList);
                    using var counterList = MemList<ulong>.Get(conditionsListPtr);

                    foreach (var childCond in counterList)
                        ParseQuestCondition(childCond, quest, completedConditions);
                }
                else if (condName == "ConditionLaunchFlare")
                {
                    var zonePtr = Memory.ReadPtr(condition + Offsets.QuestConditionLaunchFlare.zoneId);
                    var target = Memory.ReadUnityString(zonePtr);

                    var location = CreateQuestLocation(quest.Id, target);
                    if (location != null)
                    {
                        var objective = new QuestObjective
                        {
                            Id = condID,
                            Type = QuestObjectiveType.LaunchFlare,
                            Optional = isOptional,
                            Description = $"Launch flare at {target}",
                            IsCompleted = isCompleted,
                            RequiredItemIds = new List<string>(),
                            LocationObjectives = new List<QuestLocation> { location }
                        };

                        quest.Objectives.Add(objective);
                    }
                }
                else if (condName == "ConditionZone")
                {
                    var zonePtr = Memory.ReadPtr(condition + Offsets.QuestConditionZone.zoneId);
                    var targetPtr = Memory.ReadPtr(condition + Offsets.QuestConditionZone.target);
                    var zone = Memory.ReadUnityString(zonePtr);

                    var itemIds = new List<string>();
                    using var targets = MemArray<ulong>.Get(targetPtr);
                    foreach (var targetPtr2 in targets)
                        itemIds.Add(Memory.ReadUnityString(targetPtr2));

                    var location = CreateQuestLocation(quest.Id, zone);
                    var locationObjectives = location != null ? new List<QuestLocation> { location } : new List<QuestLocation>();

                    var objective = new QuestObjective
                    {
                        Id = condID,
                        Type = QuestObjectiveType.ZoneObjective,
                        Optional = isOptional,
                        Description = $"Complete objective in {zone}",
                        IsCompleted = isCompleted,
                        RequiredItemIds = itemIds,
                        LocationObjectives = locationObjectives
                    };

                    quest.Objectives.Add(objective);
                }
                else if (condName == "ConditionInZone")
                {
                    var zonePtr = Memory.ReadPtr(condition + 0x70);
                    using var zones = MemArray<ulong>.Get(zonePtr);

                    var locationObjectives = new List<QuestLocation>();
                    foreach (var zone in zones)
                    {
                        var id = Memory.ReadUnityString(zone);
                        var location = CreateQuestLocationWithOutline(quest.Id, id);
                        if (location != null)
                            locationObjectives.Add(location);
                    }

                    if (locationObjectives.Any())
                    {
                        var objective = new QuestObjective
                        {
                            Id = condID,
                            Type = QuestObjectiveType.InZone,
                            Optional = isOptional,
                            Description = "Complete objective in zone",
                            IsCompleted = isCompleted,
                            RequiredItemIds = new List<string>(),
                            LocationObjectives = locationObjectives
                        };

                        quest.Objectives.Add(objective);
                    }
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[QuestManager] ERROR parsing Condition: {ex}");
            }
        }

        private QuestLocation CreateQuestLocation(string questId, string locationId, bool optional = false)
        {
            if (_mapToId.TryGetValue(MapID, out var id) &&
                _questZones.TryGetValue(id, out var zones) &&
                zones.TryGetValue(locationId, out var location))
            {
                return new QuestLocation(questId, locationId, location, optional);
            }
            return null;
        }

        private QuestLocation CreateQuestLocationWithOutline(string questId, string locationId, bool optional = false)
        {
            if (_mapToId.TryGetValue(MapID, out var mapId) &&
                _questOutlines.TryGetValue(mapId, out var outlines) &&
                outlines.TryGetValue(locationId, out var outline) &&
                _questZones.TryGetValue(mapId, out var zones) &&
                zones.TryGetValue(locationId, out var location))
            {
                return new QuestLocation(questId, locationId, location, outline, optional);
            }
            return null;
        }

        private static FrozenDictionary<string, FrozenDictionary<string, Vector3>> GetQuestZones()
        {
            var tasks = Config.QuestHelper.KappaFilter
                ? EftDataManager.TaskData.Values.Where(task => task.KappaRequired)
                : EftDataManager.TaskData.Values;

            return tasks
                .Where(task => task.Objectives is not null)
                .SelectMany(task => task.Objectives)
                .Where(objective =>
                {
                    if (!Config.QuestHelper.OptionalTaskFilter && objective.Optional)
                        return false;

                    return objective.Zones is not null;
                })
                .SelectMany(objective => objective.Zones)
                .Where(zone => zone.Position is not null && zone.Map?.Id is not null)
                .GroupBy(zone => zone.Map.Id, zone => new
                {
                    id = zone.Id,
                    pos = new Vector3(zone.Position.X, zone.Position.Y, zone.Position.Z)
                }, StringComparer.OrdinalIgnoreCase)
                .DistinctBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group
                    .DistinctBy(x => x.id, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        zone => zone.id,
                        zone => zone.pos,
                        StringComparer.OrdinalIgnoreCase
                    ).ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase
                )
                .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        }

        private static FrozenDictionary<string, FrozenDictionary<string, List<Vector3>>> GetQuestOutlines()
        {
            var tasks = Config.QuestHelper.KappaFilter
                ? EftDataManager.TaskData.Values.Where(task => task.KappaRequired)
                : EftDataManager.TaskData.Values;

            return tasks
                .Where(task => task.Objectives is not null)
                .SelectMany(task => task.Objectives)
                .Where(objective =>
                {
                    if (!Config.QuestHelper.OptionalTaskFilter && objective.Optional)
                        return false;

                    return objective.Zones is not null;
                })
                .SelectMany(objective => objective.Zones)
                .Where(zone => zone.Outline is not null && zone.Map?.Id is not null)
                .GroupBy(zone => zone.Map.Id, zone => new
                {
                    id = zone.Id,
                    outline = zone.Outline.Select(outline => new Vector3(outline.X, outline.Y, outline.Z)).ToList()
                }, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .DistinctBy(x => x.id, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(
                            zone => zone.id,
                            zone => zone.outline,
                            StringComparer.OrdinalIgnoreCase
                        ).ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase
                )
                .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Represents a quest with its objectives and completion status.
    /// </summary>
    public sealed class Quest
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool KappaRequired { get; set; }
        public List<QuestObjective> Objectives { get; set; } = new List<QuestObjective>();
        public HashSet<string> RequiredItems { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> CompletedConditions { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// True if all objectives are completed.
        /// </summary>
        public bool IsCompleted => Objectives.All(o => o.IsCompleted);

        /// <summary>
        /// Number of completed objectives.
        /// </summary>
        public int CompletedObjectivesCount => Objectives.Count(o => o.IsCompleted);

        /// <summary>
        /// Total number of objectives.
        /// </summary>
        public int TotalObjectivesCount => Objectives.Count;
    }

    /// <summary>
    /// Represents a quest objective with its completion status and requirements.
    /// </summary>
    public sealed class QuestObjective
    {
        public string Id { get; set; } = string.Empty;
        public QuestObjectiveType Type { get; set; }
        public bool Optional { get; set; }
        public string Description { get; set; } = string.Empty;
        public bool IsCompleted { get; set; }
        public List<string> RequiredItemIds { get; set; } = new List<string>();
        public List<QuestLocation> LocationObjectives { get; set; } = new List<QuestLocation>();

        /// <summary>
        /// True if this objective has location requirements.
        /// </summary>
        public bool HasLocationRequirement => LocationObjectives.Any();

        /// <summary>
        /// True if this objective requires items.
        /// </summary>
        public bool HasItemRequirement => RequiredItemIds.Any();
    }

    /// <summary>
    /// Types of quest objectives.
    /// </summary>
    public enum QuestObjectiveType
    {
        FindItem,
        PlaceItem,
        VisitLocation,
        LaunchFlare,
        ZoneObjective,
        InZone,
        Other
    }

    /// <summary>
    /// Wraps a Quest Location marker onto the Map GUI.
    /// </summary>
    public sealed class QuestLocation : IWorldEntity, IMapEntity, IMouseoverEntity, IESPEntity
    {
        private static Config Config => Program.Config;

        private Vector3 _position;
        private List<Vector3> _outline;

        /// <summary>
        /// Original location name.
        /// </summary>
        public string LocationName { get; }

        /// <summary>
        /// Quest name for display purposes.
        /// </summary>
        public string QuestName { get; }

        /// <summary>
        /// Quest this belongs to.
        /// </summary>
        public string QuestID { get; }

        /// <summary>
        /// Map ID this location belongs to.
        /// </summary>
        public string MapId { get; }

        /// <summary>
        /// Whether this quest location comes from an optional objective.
        /// </summary>
        public bool Optional { get; }

        /// <summary>
        /// Quest location outlines (if any).
        /// </summary>
        public List<Vector3> Outline => _outline;

        public QuestLocation(string questId, string locationName, Vector3 position, bool optional = false)
        {
            QuestID = questId;
            LocationName = locationName;
            _position = position;
            Optional = optional;
            MapId = GetCurrentMapDisplayId();

            if (EftDataManager.TaskData.TryGetValue(questId, out var taskData))
                QuestName = taskData.Name ?? locationName;
            else
                QuestName = locationName;
        }

        public QuestLocation(string questId, string locationName, Vector3 position, List<Vector3> outline, bool optional = false)
        {
            QuestID = questId;
            LocationName = locationName;
            _position = position;
            _outline = outline;
            Optional = optional;
            MapId = GetCurrentMapDisplayId();

            if (EftDataManager.TaskData.TryGetValue(questId, out var taskData))
                QuestName = taskData.Name ?? locationName;
            else
                QuestName = locationName;
        }

        private string GetCurrentMapDisplayId()
        {
            var mapId = Memory.MapID ?? "unknown";
            return mapId switch
            {
                "factory4_day" => "55f2d3fd4bdc2d5f408b4567",
                "factory4_night" => "59fc81d786f774390775787e",
                "bigmap" => "56f40101d2720b2a4d8b45d6",
                "woods" => "5704e3c2d2720bac5b8b4567",
                "lighthouse" => "5704e4dad2720bb55b8b4567",
                "shoreline" => "5704e554d2720bac5b8b456e",
                "labyrinth" => "6733700029c367a3d40b02af",
                "rezervbase" => "5704e5fad2720bc05b8b4567",
                "interchange" => "5714dbc024597771384a510d",
                "tarkovstreets" => "5714dc692459777137212e12",
                "laboratory" => "5b0fc42d86f7744a585f9105",
                "Sandbox" => "653e6760052c01c1c805532f",
                "Sandbox_high" => "65b8d6f5cdde2479cb2a3125",
                _ => mapId
            };
        }

        public ref Vector3 Position => ref _position;
        public Vector2 MouseoverPosition { get; set; }

        public void Draw(SKCanvas canvas, LoneMapParams mapParams, ILocalPlayer localPlayer)
        {
            if (!Config.QuestHelper.OptionalTaskFilter && Optional)
                return;

            var dist = Vector3.Distance(localPlayer.Position, Position);
            if (dist > QuestManager.Settings.RenderDistance)
                return;

            var heightDiff = Position.Y - localPlayer.Position.Y;
            var point = Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams);
            MouseoverPosition = new Vector2(point.X, point.Y);

            if (_outline != null && _outline.Count > 2)
                DrawOutline(canvas, mapParams);

            SKPaints.ShapeOutline.StrokeWidth = 2f;
            float distanceYOffset;
            float nameXOffset = 7f * MainWindow.UIScale;
            float nameYOffset;

            const float HEIGHT_INDICATOR_THRESHOLD = 1.85f;

            if (heightDiff > HEIGHT_INDICATOR_THRESHOLD)
            {
                using var path = point.GetUpArrow(5);
                canvas.DrawPath(path, SKPaints.ShapeOutline);
                canvas.DrawPath(path, SKPaints.QuestHelperPaint);
                distanceYOffset = 18f * MainWindow.UIScale;
                nameYOffset = 6f * MainWindow.UIScale;
            }
            else if (heightDiff < -HEIGHT_INDICATOR_THRESHOLD)
            {
                using var path = point.GetDownArrow(5);
                canvas.DrawPath(path, SKPaints.ShapeOutline);
                canvas.DrawPath(path, SKPaints.QuestHelperPaint);
                distanceYOffset = 12f * MainWindow.UIScale;
                nameYOffset = 1f * MainWindow.UIScale;
            }
            else
            {
                var size = 5 * MainWindow.UIScale;
                canvas.DrawCircle(point, size, SKPaints.ShapeOutline);
                canvas.DrawCircle(point, size, SKPaints.QuestHelperPaint);
                distanceYOffset = 16f * MainWindow.UIScale;
                nameYOffset = 4f * MainWindow.UIScale;
            }

            if (QuestManager.Settings.ShowName)
            {
                point.Offset(nameXOffset, nameYOffset);
                if (!string.IsNullOrEmpty(QuestName))
                {
                    canvas.DrawText(QuestName, point, SKPaints.TextOutline);
                    canvas.DrawText(QuestName, point, SKPaints.QuestHelperText);
                }
            }

            if (QuestManager.Settings.ShowDistance)
            {
                var distText = $"{(int)dist}m";
                var distWidth = SKPaints.QuestHelperText.MeasureText($"{(int)dist}");
                var distPoint = new SKPoint(
                    point.X - (distWidth / 2) - nameXOffset,
                    point.Y + distanceYOffset - nameYOffset
                );
                canvas.DrawText(distText, distPoint, SKPaints.TextOutline);
                canvas.DrawText(distText, distPoint, SKPaints.QuestHelperText);
            }
        }

        private void DrawOutline(SKCanvas canvas, LoneMapParams mapParams)
        {
            if (_outline == null || _outline.Count < 3)
                return;

            using var path = new SKPath();
            bool first = true;

            foreach (var vertex in _outline)
            {
                var point = vertex.ToMapPos(mapParams.Map).ToZoomedPos(mapParams);
                if (first)
                {
                    path.MoveTo(point);
                    first = false;
                }
                else
                {
                    path.LineTo(point);
                }
            }

            path.Close();

            using var fillPaint = new SKPaint
            {
                Color = SKPaints.QuestHelperPaint.Color.WithAlpha(50),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };

            canvas.DrawPath(path, fillPaint);

            using var strokePaint = new SKPaint
            {
                Color = SKPaints.QuestHelperPaint.Color,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2f,
                IsAntialias = true
            };

            canvas.DrawPath(path, strokePaint);
        }

        public void DrawESP(SKCanvas canvas, LocalPlayer localPlayer)
        {
            if (!Config.QuestHelper.OptionalTaskFilter && Optional)
                return;

            var dist = Vector3.Distance(localPlayer.Position, Position);
            if (dist > QuestManager.ESPSettings.RenderDistance)
                return;

            if (!CameraManagerBase.WorldToScreen(ref _position, out var scrPos))
                return;

            var scale = ESP.Config.FontScale;

            switch (QuestManager.ESPSettings.RenderMode)
            {
                case EntityRenderMode.None:
                    break;

                case EntityRenderMode.Dot:
                    var dotSize = 3f * scale;
                    canvas.DrawCircle(scrPos.X, scrPos.Y, dotSize, SKPaints.PaintQuestHelperESP);
                    break;

                case EntityRenderMode.Cross:
                    var crossSize = 5f * scale;
                    using (var thickPaint = new SKPaint
                    {
                        Color = SKPaints.PaintQuestHelperESP.Color,
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

                case EntityRenderMode.Square:
                    var boxHalf = 3f * scale;
                    var boxPt = new SKRect(
                        scrPos.X - boxHalf, scrPos.Y - boxHalf,
                        scrPos.X + boxHalf, scrPos.Y + boxHalf);
                    canvas.DrawRect(boxPt, SKPaints.PaintQuestHelperESP);
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
                        canvas.DrawPath(diamondPath, SKPaints.PaintQuestHelperESP);
                    }
                    break;
            }

            if (QuestManager.ESPSettings.ShowName || QuestManager.ESPSettings.ShowDistance)
            {
                var textY = scrPos.Y + 16f * scale;
                var textPt = new SKPoint(scrPos.X, textY);

                textPt.DrawESPText(
                    canvas,
                    this,
                    localPlayer,
                    QuestManager.ESPSettings.ShowDistance,
                    SKPaints.TextQuestHelperESP,
                    QuestManager.ESPSettings.ShowName ? QuestName : null
                );
            }
        }

        public void DrawMouseover(SKCanvas canvas, LoneMapParams mapParams, LocalPlayer localPlayer)
        {
            string[] lines = new string[] { QuestName };
            Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams).DrawMouseoverText(canvas, lines);
        }
    }
}