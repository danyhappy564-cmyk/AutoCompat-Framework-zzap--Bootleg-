using SPTarkov.Server.Core.Models.Common;

namespace AutoCompatFramework;

/// <summary>
/// 아이템 종류(부모 분류) ID. 원작(TS)의 BaseClasses 이름을 그대로 쓴다.
/// 4.1 의 BaseClasses 와 이름이 다른 것(UBGL, GAS_BLOCK, RAIL_COVER 등)이 있어서, EFT 고정 ID 를 직접 적었다.
/// </summary>
public static class BaseClassIds
{
    public static readonly MongoId WEAPON = new("5422acb9af1c889c16000029");
    public static readonly MongoId MOD = new("5448fe124bdc2da5018b4567");
    public static readonly MongoId AMMO = new("5485a8684bdc2da71d8b4567");
    public static readonly MongoId MAGAZINE = new("5448bc234bdc2d3c308b4569");

    public static readonly MongoId FOREGRIP = new("55818af64bdc2d5b648b4570");
    public static readonly MongoId OPTIC_SCOPE = new("55818ae44bdc2dde698b456c");
    public static readonly MongoId MUZZLE = new("5448fe394bdc2d0d028b456c");
    public static readonly MongoId STOCK = new("55818a594bdc2db9688b456a");
    public static readonly MongoId BARREL = new("555ef6e44bdc2de9068b457e");
    public static readonly MongoId HANDGUARD = new("55818a104bdc2db9688b4569");
    public static readonly MongoId UBGL = new("55818b014bdc2ddc698b456b");
    public static readonly MongoId MOUNT = new("55818b224bdc2dde698b456f");
    public static readonly MongoId FLASHLIGHT = new("55818b084bdc2d5b648b4571");
    public static readonly MongoId LIGHT_LASER_DESIGNATOR = new("55818b0e4bdc2dde698b456e");
    public static readonly MongoId TACTICAL_COMBO = new("55818b164bdc2ddc698b456c");
    public static readonly MongoId BIPOD = new("55818afb4bdc2dde698b456d");
    public static readonly MongoId COMPENSATOR = new("550aa4af4bdc2dd4348b456e");
    public static readonly MongoId SILENCER = new("550aa4cd4bdc2dd8348b456c");
    public static readonly MongoId PISTOL_GRIP = new("55818a684bdc2ddd698b456d");
    public static readonly MongoId RECEIVER = new("55818a304bdc2db5418b457d");
    public static readonly MongoId CHARGING_HANDLE = new("55818a6f4bdc2db9688b456b");
    public static readonly MongoId GAS_BLOCK = new("56ea9461d2720b67698b456f");
    public static readonly MongoId RAIL_COVER = new("55818b1d4bdc2d5b648b4572");
    public static readonly MongoId SIGHTS = new("5448fe7a4bdc2d6f028b456b");
    public static readonly MongoId ASSAULT_SCOPE = new("55818add4bdc2d5b648b456f");
    public static readonly MongoId FLASH_HIDER = new("550aa4bf4bdc2dd6348b456b");
    public static readonly MongoId COLLIMATOR = new("55818ad54bdc2ddc698b4569");
    public static readonly MongoId IRON_SIGHT = new("55818ac54bdc2d5b648b456e");
    public static readonly MongoId COMPACT_COLLIMATOR = new("55818acf4bdc2dde698b456b");
    public static readonly MongoId COMB_MUZZLE_DEVICE = new("550aa4dd4bdc2dc9348b4569");
}
