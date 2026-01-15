using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WorldBoxMultiplayer
{
    public enum EntityType : byte
    {
        Actor = 1,
        City = 2,
        Kingdom = 3,
        Building = 4
    }

    public static class EntitySerializer
    {
        public static byte[] SerializeActorBasic(Actor actor)
        {
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write((byte)EntityType.Actor);
                bw.Write(actor.id);
                bw.Write((int)actor.current_position.x);
                bw.Write((int)actor.current_position.y);
                bw.Write(actor.data.health);
                bw.Write(actor.data.asset_id ?? "human");
                bw.Write(actor.data.name ?? "");
                bw.Write(actor.data.level);
                bw.Write(actor.data.cityID);
                bw.Write(actor.data.civ_kingdom_id);
                return ms.ToArray();
            }
        }

        public static void DeserializeActorBasic(byte[] data, int offset, out ActorSyncData result)
        {
            result = new ActorSyncData();
            using (var ms = new MemoryStream(data, offset, data.Length - offset))
            using (var br = new BinaryReader(ms))
            {
                result.id = br.ReadInt64();
                result.x = br.ReadInt32();
                result.y = br.ReadInt32();
                result.health = br.ReadInt32();
                result.assetId = br.ReadString();
                result.name = br.ReadString();
                result.level = br.ReadInt32();
                result.cityId = br.ReadInt64();
                result.kingdomId = br.ReadInt64();
            }
        }

        public static byte[] SerializeCity(City city)
        {
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write((byte)EntityType.City);
                bw.Write(city.id);
                bw.Write(city.data.name ?? "");
                bw.Write(city.data.kingdomID);
                bw.Write(city.status.population);
                var centerTile = city.zones.Count > 0 ? city.zones[0]?.centerTile : null;
                bw.Write(centerTile?.x ?? 0);
                bw.Write(centerTile?.y ?? 0);
                return ms.ToArray();
            }
        }

        public static void DeserializeCity(byte[] data, int offset, out CitySyncData result)
        {
            result = new CitySyncData();
            using (var ms = new MemoryStream(data, offset, data.Length - offset))
            using (var br = new BinaryReader(ms))
            {
                result.id = br.ReadInt64();
                result.name = br.ReadString();
                result.kingdomId = br.ReadInt64();
                result.population = br.ReadInt32();
                result.x = br.ReadInt32();
                result.y = br.ReadInt32();
            }
        }

        public static byte[] SerializeKingdom(Kingdom kingdom)
        {
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write((byte)EntityType.Kingdom);
                bw.Write(kingdom.id);
                bw.Write(kingdom.data.name ?? "");
                bw.Write(kingdom.data.color_id);
                bw.Write(kingdom.data.banner_icon_id);
                bw.Write(kingdom.data.kingID);
                bw.Write(kingdom.data.capitalID);
                bw.Write(kingdom.data.original_actor_asset ?? "human");
                return ms.ToArray();
            }
        }

        public static void DeserializeKingdom(byte[] data, int offset, out KingdomSyncData result)
        {
            result = new KingdomSyncData();
            using (var ms = new MemoryStream(data, offset, data.Length - offset))
            using (var br = new BinaryReader(ms))
            {
                result.id = br.ReadInt64();
                result.name = br.ReadString();
                result.colorId = br.ReadInt32();
                result.bannerId = br.ReadInt32();
                result.kingId = br.ReadInt64();
                result.capitalId = br.ReadInt64();
                result.actorAsset = br.ReadString();
            }
        }

        public static byte[] SerializeBuilding(Building building)
        {
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write((byte)EntityType.Building);
                bw.Write(building.id);
                bw.Write(building.data.mainX);
                bw.Write(building.data.mainY);
                bw.Write(building.asset.id ?? "");
                bw.Write(building.data.health);
                bw.Write(building.data.cityID);
                return ms.ToArray();
            }
        }

        public static void DeserializeBuilding(byte[] data, int offset, out BuildingSyncData result)
        {
            result = new BuildingSyncData();
            using (var ms = new MemoryStream(data, offset, data.Length - offset))
            using (var br = new BinaryReader(ms))
            {
                result.id = br.ReadInt64();
                result.x = br.ReadInt32();
                result.y = br.ReadInt32();
                result.assetId = br.ReadString();
                result.health = br.ReadInt32();
                result.cityId = br.ReadInt64();
            }
        }
    }

    public struct ActorSyncData
    {
        public long id;
        public int x, y;
        public int health;
        public string assetId;
        public string name;
        public int level;
        public long cityId;
        public long kingdomId;
    }

    public struct CitySyncData
    {
        public long id;
        public string name;
        public long kingdomId;
        public int population;
        public int x, y;
    }

    public struct KingdomSyncData
    {
        public long id;
        public string name;
        public int colorId;
        public int bannerId;
        public long kingId;
        public long capitalId;
        public string actorAsset;
    }

    public struct BuildingSyncData
    {
        public long id;
        public int x, y;
        public string assetId;
        public int health;
        public long cityId;
    }
}
