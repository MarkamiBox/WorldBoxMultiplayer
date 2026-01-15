using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using HarmonyLib;

namespace WorldBoxMultiplayer
{
    public class ClientController : MonoBehaviour
    {
        public static ClientController Instance;
        
        public bool IsClientMode = false;
        
        private Dictionary<long, Vector2> _actorTargetPositions = new Dictionary<long, Vector2>();
        private Dictionary<long, ActorSyncData> _pendingActors = new Dictionary<long, ActorSyncData>();
        private HashSet<long> _knownActorIds = new HashSet<long>();
        private HashSet<long> _knownCityIds = new HashSet<long>();
        private HashSet<long> _knownKingdomIds = new HashSet<long>();
        
        private float _interpolationSpeed = 10f;

        void Awake() { Instance = this; }

        void Update()
        {
            if (!IsClientMode) return;
            InterpolateActorPositions();
        }

        private void InterpolateActorPositions()
        {
            foreach (var actor in World.world.units)
            {
                if (actor == null || !actor.isAlive()) continue;
                
                if (_actorTargetPositions.TryGetValue(actor.id, out Vector2 target))
                {
                    Vector2 current = actor.current_position;
                    float dist = Vector2.Distance(current, target);
                    
                    if (dist > 0.1f && dist < 20f)
                    {
                        Vector2 newPos = Vector2.Lerp(current, target, Time.deltaTime * _interpolationSpeed);
                        actor.current_position = newPos;
                    }
                    else if (dist >= 20f)
                    {
                        actor.current_position = target;
                    }
                }
            }
        }

        public void OnDeltaReceived(byte[] compressedData)
        {
            try
            {
                byte[] data = Decompress(compressedData);
                ProcessDeltaPacket(data);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Client] Delta processing error: {e.Message}");
            }
        }

        private void ProcessDeltaPacket(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            using (var br = new BinaryReader(ms))
            {
                byte packetType = br.ReadByte();
                if (packetType != (byte)PacketType.STATE_DELTA) return;
                
                ushort entityCount = br.ReadUInt16();
                
                for (int i = 0; i < entityCount; i++)
                {
                    if (ms.Position >= ms.Length) break;
                    
                    byte entityType = br.ReadByte();
                    ms.Position--;
                    
                    int remaining = (int)(data.Length - ms.Position);
                    byte[] entityData = new byte[remaining];
                    Array.Copy(data, ms.Position, entityData, 0, remaining);
                    
                    switch ((EntityType)entityType)
                    {
                        case EntityType.Actor:
                            EntitySerializer.DeserializeActorBasic(entityData, 1, out ActorSyncData actorData);
                            ApplyActorState(actorData);
                            break;
                        case EntityType.City:
                            EntitySerializer.DeserializeCity(entityData, 1, out CitySyncData cityData);
                            ApplyCityState(cityData);
                            break;
                        case EntityType.Kingdom:
                            EntitySerializer.DeserializeKingdom(entityData, 1, out KingdomSyncData kingdomData);
                            ApplyKingdomState(kingdomData);
                            break;
                    }
                    
                    ms.Position += remaining;
                }
            }
        }

        private void ApplyActorState(ActorSyncData data)
        {
            Actor actor = null;
            foreach (var a in World.world.units)
            {
                if (a.id == data.id) { actor = a; break; }
            }
            
            if (actor == null)
            {
                _pendingActors[data.id] = data;
                _knownActorIds.Add(data.id);
                return;
            }
            
            _actorTargetPositions[data.id] = new Vector2(data.x, data.y);
            
            if (actor.data.health != data.health)
                actor.data.health = data.health;
            if (actor.data.level != data.level)
                actor.data.level = data.level;
            if (actor.data.name != data.name && !string.IsNullOrEmpty(data.name))
                actor.data.name = data.name;
            
            _knownActorIds.Add(data.id);
        }

        private void ApplyCityState(CitySyncData data)
        {
            City city = World.world.cities.get(data.id);
            
            if (city == null)
            {
                _knownCityIds.Add(data.id);
                return;
            }
            
            if (city.data.name != data.name && !string.IsNullOrEmpty(data.name))
                city.data.name = data.name;
            if (city.data.kingdomID != data.kingdomId)
                city.data.kingdomID = data.kingdomId;
            
            _knownCityIds.Add(data.id);
        }

        private void ApplyKingdomState(KingdomSyncData data)
        {
            Kingdom kingdom = World.world.kingdoms.get(data.id);
            
            if (kingdom == null)
            {
                _knownKingdomIds.Add(data.id);
                return;
            }
            
            if (kingdom.data.name != data.name && !string.IsNullOrEmpty(data.name))
                kingdom.data.name = data.name;
            if (kingdom.data.color_id != data.colorId)
            {
                kingdom.data.color_id = data.colorId;
                ColorAsset color = AssetManager.kingdom_colors_library.get(data.colorId.ToString());
                if (color != null) kingdom.updateColor(color);
            }
            if (kingdom.data.banner_icon_id != data.bannerId)
            {
                kingdom.data.banner_icon_id = data.bannerId;
                kingdom.generateBanner();
            }
            
            _knownKingdomIds.Add(data.id);
        }

        public void OnEntityRemoved(EntityType type, long id)
        {
            switch (type)
            {
                case EntityType.Actor:
                    _actorTargetPositions.Remove(id);
                    _knownActorIds.Remove(id);
                    foreach (var actor in World.world.units)
                    {
                        if (actor.id == id)
                        {
                            actor.getHitFullHealth(AttackType.Divine);
                            break;
                        }
                    }
                    break;
            }
        }

        public void SendInputAction(string powerID, int x, int y)
        {
            if (!NetworkManager.Instance.IsMultiplayerReady) return;
            NetworkManager.Instance.SendRaw($"I|{powerID}|{x}|{y}\n");
        }

        public void ClearState()
        {
            _actorTargetPositions.Clear();
            _pendingActors.Clear();
            _knownActorIds.Clear();
            _knownCityIds.Clear();
            _knownKingdomIds.Clear();
            IsClientMode = false;
        }

        private byte[] Decompress(byte[] data)
        {
            try
            {
                using (var input = new MemoryStream(data))
                using (var gzip = new GZipStream(input, CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    gzip.CopyTo(output);
                    return output.ToArray();
                }
            }
            catch
            {
                return data;
            }
        }
    }
}
