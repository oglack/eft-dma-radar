using eft_dma_shared.Common.Misc;
using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_shared.Common.DMA.ScatterAPI;
using eft_dma_shared.Common.Unity.Collections;
using eft_dma_radar.Tarkov.Features.MemoryWrites.Patches;
using eft_dma_shared.Common.Unity.LowLevel.Hooks;

namespace eft_dma_radar.Tarkov.GameWorld
{
    public sealed class RegisteredPlayers : IReadOnlyCollection<Player>
    {
        #region Fields/Properties/Constructor

        public static implicit operator ulong(RegisteredPlayers x) => x.Base;
        private ulong Base { get; }
        private readonly LocalGameWorld _game;
        private readonly ConcurrentDictionary<ulong, Player> _players = new();

        /// <summary>
        /// LocalPlayer Instance.
        /// </summary>
        public LocalPlayer LocalPlayer { get; }

        /// <summary>
        /// RegisteredPlayers List Constructor.
        /// </summary>
        public RegisteredPlayers(ulong baseAddr, LocalGameWorld game)
        {
            Base = baseAddr;
            _game = game;
            var mainPlayer = Memory.ReadPtr(_game + Offsets.ClientLocalGameWorld.MainPlayer, false);
            var localPlayer = new LocalPlayer(mainPlayer);
            _players[localPlayer] = LocalPlayer = localPlayer;
        }

        #endregion

        /// <summary>
        /// Updates the ConcurrentDictionary of 'Players'
        /// </summary>
        public void Refresh()
        {
            try
            {
                using var playersList = MemList<ulong>.Get(this, false); // Realtime Read
                var registered = playersList.Where(x => x != 0x0).ToHashSet();
                int i = -1;
                // Allocate New Players
                foreach (var playerBase in registered)
                {
                    if (playerBase == LocalPlayer) // Skip LocalPlayer, already allocated
                        continue;
                    i++;
                    if (_players.TryGetValue(playerBase, out var existingPlayer)) // Player already exists
                    {
                        if (existingPlayer.ErrorTimer.ElapsedMilliseconds >= 1500) // Erroring out a lot? Re-Alloc
                        {
                            LoneLogging.WriteLine($"WARNING - Existing player '{existingPlayer.Name}' being re-allocated due to excessive errors...");
                            Player.Allocate(_players, playerBase);
                        }
                        // Nothing else needs to happen here
                    }
                    else // Add New Player
                    {

                        Player.Allocate(_players, playerBase);
                        LoneLogging.WriteLine($"New Player Allocated: {i} - {playerBase:X}");
                        foreach(var player in _players.Values)
                        {
                            if (player.ListIndex == i) // Ensure ListIndex is set correctly
                                continue;
                            player.ListIndex = i; // Set ListIndex for new player
                        }
                    }
                }

                // Update Existing Players including LocalPlayer
                UpdateExistingPlayers(registered);

                if (VisibilityLinecast.Initilized)
                {
                    try
                    {
                        var localPlayer = Memory.LocalPlayer;
                        var viewDirection = Vector3.Normalize(RotationToDirection(localPlayer.Rotation));
                        if (localPlayer?.Firearm?.FireportPosition is not Vector3 fireportPos)
                        {
                            //LoneLogging.WriteLine("[Visibility] No valid fireport position available");
                            return;
                        }

                        // Get all active players from our managed collection (_players)
                        var activePlayers = _players.Values
                            .Where(p => p != null && p.IsActive && p.IsAlive && !(p is LocalPlayer))
                            .ToList();

                        if (activePlayers.Count == 0)
                        {
                            //LoneLogging.WriteLine("[Visibility] No active players to check");
                            return;
                        }

                        //LoneLogging.WriteLine($"[Visibility] Checking visibility for {activePlayers.Count} players");
                        VisibilityManager.UpdateVisibilityForPlayers(activePlayers, fireportPos, viewDirection);
                    }
                    catch (Exception ex)
                    {
                        LoneLogging.WriteLine($"[VisCheck] ERROR Updating Visibility: {ex}");
                        NotificationsShared.Error("Error updating visibility! Check logs for details.");
                    }
                }                
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"CRITICAL ERROR - RegisteredPlayers Loop FAILED: {ex}");
            }
        }
        public static Vector3 RotationToDirection(Vector2 rotation)
        {
            // Convert rotation (yaw, pitch) to a direction vector
            // This might need adjustments based on how you define rotation
            var yaw = (float)rotation.X.ToRadians();
            var pitch = (float)rotation.Y.ToRadians();
            Vector3 direction;
            direction.X = (float)(Math.Cos(pitch) * Math.Sin(yaw));
            direction.Y = (float)Math.Sin(-pitch); // Negative pitch because in Unity, as pitch increases, we look down
            direction.Z = (float)(Math.Cos(pitch) * Math.Cos(yaw));

            return Vector3.Normalize(direction);
        }
        /// <summary>
        /// Returns the Player Count currently in the Registered Players List.
        /// </summary>
        /// <returns>Count of players.</returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public int GetPlayerCount()
        {
            var count = Memory.ReadValue<int>(this + MemList<byte>.CountOffset, false);
            if (count < 0 || count > 256)
                throw new ArgumentOutOfRangeException(nameof(count));
            return count;
        }

        /// <summary>
        /// Scans the existing player list and updates Players as needed.
        /// </summary>
        private void UpdateExistingPlayers(IReadOnlySet<ulong> registered)
        {
            var allPlayers = _players.Values;
            if (allPlayers.Count == 0)
                return;
            using var map = ScatterReadMap.Get();
            var round1 = map.AddRound(false);
            int i = 0;
            foreach (var player in allPlayers)
            {
                player.OnRegRefresh(round1[i++], registered);
            }
            map.Execute();
        }

        /// <summary>
        /// Checks if there is an existing BTR player in the Players Dictionary, and if not, it is allocated and swapped.
        /// </summary>
        /// <param name="btrPlayerBase">Player Base Addr for BTR Operator.</param>
        public void TryAllocateBTR(ulong btrView, ulong btrPlayerBase)
        {
            if (_players.TryGetValue(btrPlayerBase, out var existing) && existing is not BtrOperator)
            {
                var btr = new BtrOperator(btrView, btrPlayerBase);
                _players[btrPlayerBase] = btr;
                LoneLogging.WriteLine("BTR Allocated!");
            }
        }

        #region IReadOnlyCollection
        public int Count => _players.Values.Count;
        public IEnumerator<Player> GetEnumerator() =>
            _players.Values.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        #endregion
    }
}
