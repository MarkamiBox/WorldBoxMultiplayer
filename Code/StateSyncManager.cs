using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace WorldBoxMultiplayer
{
    public class StateSyncManager : MonoBehaviour
    {
        public static StateSyncManager Instance;
        
        public float SyncInterval = 0.2f;
        public bool IsSyncing = false;
        
        private float _lastSyncTime = 0f;
        private Dictionary<long, int> _actorHashes = new Dictionary<long, int>();
        private Dictionary<long, int> _cityHashes = new Dictionary<long, int>();
        private Dictionary<long, int> _kingdomHashes = new Dictionary<long, int>();
        private Dictionary<long, int> _buildingHashes = new Dictionary<long, int>();
        
        private List<byte[]> _pendingPackets = new List<byte[]>();
        private const int MAX_PACKET_SIZE = 16384;

        void Awake() { Instance = this; }

        void Update()
        {
            if (!NetworkManager.Instance.IsHost() || !NetworkManager.Instance.IsConnected)
                return;
                
            if (Time.time - _lastSyncTime > SyncInterval)
            {
                _lastSyncTime = Time.time;
                CaptureAndSendDelta();
            }
        }

        public void CaptureAndSendDelta()
        {
            if (World.world == null) return;
            
            IsSyncing = true;
            _pendingPackets.Clear();
            
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write((byte)PacketType.STATE_DELTA);
                
                int changeCount = 0;
                long countPosition = ms.Position;
                bw.Write((ushort)0);
                
                changeCount += WriteActorDeltas(bw);
                changeCount += WriteCityDeltas(bw);
                changeCount += WriteKingdomDeltas(bw);
                
                if (changeCount > 0)
                {
                    long endPosition = ms.Position;
                    ms.Position = countPosition;
                    bw.Write((ushort)changeCount);
                    ms.Position = endPosition;
                    
                    byte[] data = ms.ToArray();
                    SendDeltaPacket(data);
                }
            }
            
            CleanupRemovedEntities();
            IsSyncing = false;
        }

        private int WriteActorDeltas(BinaryWriter bw)
        {
            int count = 0;
            foreach (var actor in World.world.units)
            {
                if (actor == null || !actor.isAlive()) continue;
                
                int hash = ComputeActorHash(actor);
                bool isNew = !_actorHashes.ContainsKey(actor.id);
                bool changed = isNew || _actorHashes[actor.id] != hash;
                
                if (changed)
                {
                    _actorHashes[actor.id] = hash;
                    byte[] actorData = EntitySerializer.SerializeActorBasic(actor);
                    bw.Write(actorData);
                    count++;
                }
            }
            return count;
        }

        private int WriteCityDeltas(BinaryWriter bw)
        {
            int count = 0;
            foreach (var city in World.world.cities)
            {
                if (city == null) continue;
                
                int hash = ComputeCityHash(city);
                bool isNew = !_cityHashes.ContainsKey(city.id);
                bool changed = isNew || _cityHashes[city.id] != hash;
                
                if (changed)
                {
                    _cityHashes[city.id] = hash;
                    byte[] cityData = EntitySerializer.SerializeCity(city);
                    bw.Write(cityData);
                    count++;
                }
            }
            return count;
        }

        private int WriteKingdomDeltas(BinaryWriter bw)
        {
            int count = 0;
            foreach (var kingdom in World.world.kingdoms)
            {
                if (kingdom == null) continue;
                
                int hash = ComputeKingdomHash(kingdom);
                bool isNew = !_kingdomHashes.ContainsKey(kingdom.id);
                bool changed = isNew || _kingdomHashes[kingdom.id] != hash;
                
                if (changed)
                {
                    _kingdomHashes[kingdom.id] = hash;
                    byte[] kingdomData = EntitySerializer.SerializeKingdom(kingdom);
                    bw.Write(kingdomData);
                    count++;
                }
            }
            return count;
        }

        private void SendDeltaPacket(byte[] data)
        {
            if (data.Length > 1024)
            {
                byte[] compressed = Compress(data);
                NetworkManager.Instance.SendRaw("D|" + Convert.ToBase64String(compressed) + "\n");
            }
            else
            {
                NetworkManager.Instance.SendRaw("D|" + Convert.ToBase64String(data) + "\n");
            }
        }

        private void CleanupRemovedEntities()
        {
            List<long> toRemove = new List<long>();
            foreach (var id in _actorHashes.Keys)
            {
                bool found = false;
                foreach (var actor in World.world.units)
                {
                    if (actor.id == id && actor.isAlive()) { found = true; break; }
                }
                if (!found) toRemove.Add(id);
            }
            
            foreach (var id in toRemove)
            {
                _actorHashes.Remove(id);
                SendEntityRemoved(EntityType.Actor, id);
            }
        }

        private void SendEntityRemoved(EntityType type, long id)
        {
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write((byte)PacketType.ENTITY_REMOVED);
                bw.Write((byte)type);
                bw.Write(id);
                NetworkManager.Instance.SendRaw("R|" + Convert.ToBase64String(ms.ToArray()) + "\n");
            }
        }

        public void ClearState()
        {
            _actorHashes.Clear();
            _cityHashes.Clear();
            _kingdomHashes.Clear();
            _buildingHashes.Clear();
        }

        private int ComputeActorHash(Actor a)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (int)a.current_position.x;
                hash = hash * 31 + (int)a.current_position.y;
                hash = hash * 31 + a.data.health;
                hash = hash * 31 + a.data.level;
                hash = hash * 31 + (a.data.cityID.GetHashCode());
                return hash;
            }
        }

        private int ComputeCityHash(City c)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + c.status.population;
                hash = hash * 31 + (c.data.name?.GetHashCode() ?? 0);
                hash = hash * 31 + c.data.kingdomID.GetHashCode();
                return hash;
            }
        }

        private int ComputeKingdomHash(Kingdom k)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + k.data.color_id;
                hash = hash * 31 + (k.data.name?.GetHashCode() ?? 0);
                hash = hash * 31 + k.data.kingID.GetHashCode();
                hash = hash * 31 + k.data.capitalID.GetHashCode();
                return hash;
            }
        }

        private byte[] Compress(byte[] data)
        {
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, System.IO.Compression.CompressionLevel.Fastest))
                {
                    gzip.Write(data, 0, data.Length);
                }
                return output.ToArray();
            }
        }

        public void OnClientInputReceived(string powerID, int x, int y)
        {
            if (!NetworkManager.Instance.IsHost()) return;
            
            WorldTile tile = World.world.GetTile(x, y);
            if (tile == null) return;
            
            GodPower power = AssetManager.powers.get(powerID);
            if (power != null && power.click_action != null)
            {
                power.click_action(tile, powerID);
            }
        }
    }

    public enum PacketType : byte
    {
        FULL_SYNC = 0x01,
        STATE_DELTA = 0x02,
        INPUT_ACTION = 0x03,
        CURSOR_POS = 0x04,
        ENTITY_REMOVED = 0x05
    }
}
