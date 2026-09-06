using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using Rewired;
using InputFramework;

namespace SLine
{
    [BepInPlugin("com.sline", "SLine Target Mod", "1.6")]
    public class SLineMod : BaseUnityPlugin
    {
        public enum LineCategory
        {
            Aircraft,
            Ground,
            Ship,
            Missile,
            CruiseMissile
        }

        public sealed class UnitFilterInfo
        {
            public readonly LineCategory Category;
            public ConfigEntry<bool> WhitelistEntry;

            public UnitFilterInfo(LineCategory category, ConfigEntry<bool> whitelistEntry)
            {
                Category = category;
                WhitelistEntry = whitelistEntry;
            }
        }

        public static ConfigEntry<bool> GlobalToggle;
        public static ConfigEntry<float> LineThickness;
        public static ConfigEntry<bool> ShowFriendlyOnly;
        public static ConfigEntry<bool> EnableUnitWhitelist;
        public static ConfigEntry<float> LineUpdateInterval;

        public static ConfigEntry<bool> AircraftHold;
        public static bool AircraftToggled = false;

        public static ConfigEntry<bool> GroundHold;
        public static bool GroundToggled = false;

        public static ConfigEntry<bool> ShipHold;
        public static bool ShipToggled = false;

        public static ConfigEntry<bool> MissileHold;
        public static bool MissileToggled = false;

        public static ConfigEntry<bool> CruiseMissileHold;
        public static bool CruiseMissileToggled = false;

        public static readonly Dictionary<string, ConfigEntry<bool>> UnitWhitelists =
            new Dictionary<string, ConfigEntry<bool>>();

        private static readonly Dictionary<UnitDefinition, UnitFilterInfo> UnitFilterCache =
            new Dictionary<UnitDefinition, UnitFilterInfo>();

        public static SLineMod Instance;
        public static bool MapExists;

        private void Awake()
        {
            Instance = this;

            GlobalToggle = Config.Bind("1. Global Settings", "Global Toggle", true, "Master switch to show/hide lines by default.");
            LineThickness = Config.Bind("1. Global Settings", "Line Thickness", 0.1f, "Thickness of the lines drawn on the map.");
            ShowFriendlyOnly = Config.Bind("1. Global Settings", "Show Friendly Only", false, "Only show lines belonging to friendly units.");
            EnableUnitWhitelist = Config.Bind("1. Global Settings", "Enable Unit Whitelist", true,
                "Enable per unit whitelist. Disable this to skip individual unit type whitelist checks.");
            LineUpdateInterval = Config.Bind("1. Global Settings", "Line Update Interval", 0.05f,
                new ConfigDescription(
                    "Seconds between SLine map updates. 0 updates every frame. 0.05 = 20 updates per second.",
                    new AcceptableValueRange<float>(0f, 1f)));

            AircraftHold = Config.Bind("2. Keybinds", "Aircraft Lines Hold Mode", false, "If true, key must be held instead of toggled.");
            GroundHold = Config.Bind("2. Keybinds", "Ground Lines Hold Mode", false, "If true, key must be held instead of toggled.");
            ShipHold = Config.Bind("2. Keybinds", "Ship Lines Hold Mode", false, "If true, key must be held instead of toggled.");
            MissileHold = Config.Bind("2. Keybinds", "Missile Lines Hold Mode", false, "If true, key must be held instead of toggled.");
            CruiseMissileHold = Config.Bind("2. Keybinds", "Cruise Missile Lines Hold Mode", false, "If true, key must be held instead of toggled.");

            ExtraInputManager.LoadPendingActions();
            ExtraInputManager.RegisterAction("ToggleAircraftLines", Rewired.InputActionType.Button, "Debug");
            ExtraInputManager.RegisterAction("ToggleGroundLines", Rewired.InputActionType.Button, "Debug");
            ExtraInputManager.RegisterAction("ToggleShipLines", Rewired.InputActionType.Button, "Debug");
            ExtraInputManager.RegisterAction("ToggleMissileLines", Rewired.InputActionType.Button, "Debug");
            ExtraInputManager.RegisterAction("ToggleCruiseMissileLines", Rewired.InputActionType.Button, "Debug");

            var harmony = new Harmony("com.sline");
            harmony.PatchAll();
            StartCoroutine(ScanRoutine());
            Logger.LogInfo("SLine Mod Initialized with extra Rewired keybinding system");
        }

        private IEnumerator ScanRoutine()
        {
            yield return new WaitForSeconds(5f);

            if (EnableUnitWhitelist.Value)
                ScanAllUnitDefinitions();

            while (true)
            {
                yield return new WaitForSeconds(30f);

                if (EnableUnitWhitelist.Value)
                    ScanAllUnitDefinitions();
            }
        }

        private void ScanAllUnitDefinitions()
        {
            var defs = Resources.FindObjectsOfTypeAll<UnitDefinition>();
            foreach (var def in defs)
            {
                if (def == null)
                    continue;

                GetOrCreateUnitFilter(def, true);
            }

            Logger.LogInfo($"Pre-scanned {UnitWhitelists.Count} unit definitions into whitelist.");
        }

        private void Update()
        {
            if (!MapExists) return;

            bool inChat = false;
            try { inChat = CursorManager.GetFlag(CursorFlags.Chat); } catch { }
            if (inChat) return;

            Rewired.Player localPlayer = ReInput.players.GetPlayer(0);
            if (localPlayer == null) return;

            if (AircraftHold.Value)
                AircraftToggled = localPlayer.GetButton("ToggleAircraftLines");
            else if (localPlayer.GetButtonDown("ToggleAircraftLines"))
                AircraftToggled = !AircraftToggled;

            if (GroundHold.Value)
                GroundToggled = localPlayer.GetButton("ToggleGroundLines");
            else if (localPlayer.GetButtonDown("ToggleGroundLines"))
                GroundToggled = !GroundToggled;

            if (ShipHold.Value)
                ShipToggled = localPlayer.GetButton("ToggleShipLines");
            else if (localPlayer.GetButtonDown("ToggleShipLines"))
                ShipToggled = !ShipToggled;

            if (MissileHold.Value)
                MissileToggled = localPlayer.GetButton("ToggleMissileLines");
            else if (localPlayer.GetButtonDown("ToggleMissileLines"))
                MissileToggled = !MissileToggled;

            if (CruiseMissileHold.Value)
                CruiseMissileToggled = localPlayer.GetButton("ToggleCruiseMissileLines");
            else if (localPlayer.GetButtonDown("ToggleCruiseMissileLines"))
                CruiseMissileToggled = !CruiseMissileToggled;
        }

        public static string SanitizeConfigKey(string s)
        {
            if (string.IsNullOrEmpty(s)) return "Unknown";
            return s.Replace("=", "").Replace("\n", "").Replace("\t", "").Replace("\\", "")
                    .Replace("\"", "").Replace("'", "").Replace("[", "(").Replace("]", ")").Trim();
        }

        public static UnitFilterInfo GetOrCreateUnitFilter(UnitDefinition definition, bool createWhitelist)
        {
            if (definition == null)
                return null;

            UnitFilterInfo info;
            if (!UnitFilterCache.TryGetValue(definition, out info))
            {
                info = new UnitFilterInfo(GetCategory(definition), null);
                UnitFilterCache.Add(definition, info);
            }

            if (createWhitelist && info.WhitelistEntry == null && Instance != null)
                info.WhitelistEntry = Instance.GetOrAddWhitelist(info.Category, definition);

            return info;
        }

        private static LineCategory GetCategory(UnitDefinition definition)
        {
            if (definition is AircraftDefinition)
                return LineCategory.Aircraft;

            if (definition is ShipDefinition)
                return LineCategory.Ship;

            if (definition is MissileDefinition)
            {
                if (definition.unitPrefab != null)
                {
                    var seeker = definition.unitPrefab.GetComponent<MissileSeeker>();
                    if (seeker != null && seeker.GetSeekerType() == "INS / Opt.")
                        return LineCategory.CruiseMissile;
                }

                return LineCategory.Missile;
            }

            return LineCategory.Ground;
        }

        private ConfigEntry<bool> GetOrAddWhitelist(LineCategory category, UnitDefinition definition)
        {
            string safeCategory = GetCategoryName(category);

            string unitName = definition.unitName;
            if (string.IsNullOrEmpty(unitName))
                unitName = definition.name;

            string safeUnitName = SanitizeConfigKey(unitName);
            string key = safeCategory + "_" + safeUnitName;

            ConfigEntry<bool> entry;
            if (!UnitWhitelists.TryGetValue(key, out entry))
            {
                entry = Config.Bind("3. Whitelist: " + safeCategory, safeUnitName, true, "Enable SLine originating from " + safeUnitName);
                UnitWhitelists.Add(key, entry);
            }

            return entry;
        }

        public static string GetCategoryName(LineCategory category)
        {
            switch (category)
            {
                case LineCategory.Aircraft: return "Aircraft";
                case LineCategory.Ship: return "Ship";
                case LineCategory.Missile: return "Missile";
                case LineCategory.CruiseMissile: return "CruiseMissile";
                default: return "Ground";
            }
        }
    }

    [HarmonyPatch(typeof(DynamicMap), "Update")]
    public class DynamicMap_Update_Patch
    {
        private sealed class LineState
        {
            public GameObject GameObject;
            public Image Image;
            public RectTransform Rect;
            public bool Visible;
            public bool HasColor;
            public Color Color;
        }

        private static readonly Dictionary<UnitMapIcon, LineState> Lines =
            new Dictionary<UnitMapIcon, LineState>();

        private static float nextUpdateTime;

        public static void Postfix(DynamicMap __instance)
        {
            try
            {
                float interval = SLineMod.LineUpdateInterval.Value;
                if (interval > 0f)
                {
                    float now = Time.unscaledTime;
                    if (now < nextUpdateTime)
                        return;

                    nextUpdateTime = now + interval;
                }

                var icons = __instance.mapIcons;
                if (icons == null)
                    return;

                bool globalShow = SLineMod.GlobalToggle.Value;
                bool showAircraft = globalShow ^ SLineMod.AircraftToggled;
                bool showGround = globalShow ^ SLineMod.GroundToggled;
                bool showShip = globalShow ^ SLineMod.ShipToggled;
                bool showMissile = globalShow ^ SLineMod.MissileToggled;
                bool showCruiseMissile = globalShow ^ SLineMod.CruiseMissileToggled;

                if (!showAircraft && !showGround && !showShip && !showMissile && !showCruiseMissile)
                {
                    HideAllLines();
                    return;
                }

                bool friendlyOnly = SLineMod.ShowFriendlyOnly.Value;
                bool useWhitelist = SLineMod.EnableUnitWhitelist.Value;
                float thickness = SLineMod.LineThickness.Value;

                for (int i = 0; i < icons.Count; i++)
                {
                    var icon = icons[i] as UnitMapIcon;
                    if (icon == null)
                        continue;

                    var unit = icon.unit;
                    if (unit == null || !icon.isActiveAndEnabled)
                    {
                        HideLine(icon);
                        continue;
                    }

                    var filter = SLineMod.GetOrCreateUnitFilter(unit.definition, useWhitelist);
                    if (filter == null || !CategoryVisible(filter.Category,
                            showAircraft, showGround, showShip, showMissile, showCruiseMissile))
                    {
                        HideLine(icon);
                        continue;
                    }

                    if (friendlyOnly && !GameManager.IsLocalHQ(unit.NetworkHQ))
                    {
                        HideLine(icon);
                        continue;
                    }

                    if (useWhitelist && filter.WhitelistEntry != null && !filter.WhitelistEntry.Value)
                    {
                        HideLine(icon);
                        continue;
                    }

                    Unit target = GetTarget(unit);
                    if (target == null)
                    {
                        HideLine(icon);
                        continue;
                    }

                    UnitMapIcon targetIcon;
                    if (!DynamicMap.TryGetMapIcon(target, out targetIcon) ||
                        targetIcon == null || !targetIcon.isActiveAndEnabled)
                    {
                        HideLine(icon);
                        continue;
                    }

                    UpdateLine(icon, targetIcon, target, thickness);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError("[SLine Mod] Error in DynamicMap_Update_Patch: " + e.Message + "\n" + e.StackTrace);
            }
        }

        private static bool CategoryVisible(
            SLineMod.LineCategory category,
            bool showAircraft,
            bool showGround,
            bool showShip,
            bool showMissile,
            bool showCruiseMissile)
        {
            switch (category)
            {
                case SLineMod.LineCategory.Aircraft: return showAircraft;
                case SLineMod.LineCategory.Ship: return showShip;
                case SLineMod.LineCategory.Missile: return showMissile;
                case SLineMod.LineCategory.CruiseMissile: return showCruiseMissile;
                default: return showGround;
            }
        }

        private static Unit GetTarget(Unit unit)
        {
            var missile = unit as Missile;
            if (missile != null)
            {
                Unit target;
                return missile.targetID.TryGetUnit(out target) ? target : null;
            }

            var aircraft = unit as Aircraft;
            if (aircraft != null && aircraft.weaponManager != null)
            {
                var targets = aircraft.weaponManager.GetTargetList();
                if (targets != null && targets.Count > 0)
                    return targets[0];
            }

            return null;
        }

        private static void UpdateLine(UnitMapIcon strikerIcon, UnitMapIcon targetIcon, Unit target, float thickness)
        {
            LineState state;
            if (!Lines.TryGetValue(strikerIcon, out state) || state == null || state.GameObject == null)
            {
                state = CreateLine(strikerIcon.transform.parent);
                Lines[strikerIcon] = state;
            }

            Vector3 startPos = strikerIcon.transform.localPosition;
            Vector3 endPos = targetIcon.transform.localPosition;
            Vector3 diff = endPos - startPos;
            float distance = diff.magnitude;

            if (distance < 5f)
            {
                SetVisible(state, false);
                return;
            }

            SetVisible(state, true);

            Color color = GetLineColor(strikerIcon.unit, target);
            if (!state.HasColor || state.Color != color)
            {
                state.Image.color = color;
                state.Color = color;
                state.HasColor = true;
            }

            float angle = Mathf.Atan2(diff.y, diff.x) * Mathf.Rad2Deg;
            state.Rect.localPosition = startPos;
            state.Rect.localRotation = Quaternion.Euler(0f, 0f, angle);
            state.Rect.sizeDelta = new Vector2(distance, thickness);
        }

        private static Color GetLineColor(Unit striker, Unit target)
        {
            if (target is Aircraft)
                return striker is Missile
                    ? new Color(0f, 1f, 1f, 0.8f)
                    : new Color(1f, 1f, 1f, 0.8f);

            if (striker is Aircraft)
                return new Color(1f, 0f, 1f, 0.8f);

            if (target is Missile)
                return new Color(0f, 1f, 1f, 0.8f);

            if (target is Ship)
                return new Color(1f, 0f, 0f, 0.8f);

            return new Color(1f, 1f, 0f, 0.8f);
        }

        private static void HideLine(UnitMapIcon icon)
        {
            LineState state;
            if (Lines.TryGetValue(icon, out state) && state != null)
                SetVisible(state, false);
        }

        private static void HideAllLines()
        {
            foreach (var pair in Lines)
            {
                if (pair.Value != null)
                    SetVisible(pair.Value, false);
            }
        }

        private static void SetVisible(LineState state, bool visible)
        {
            if (state.GameObject == null || state.Visible == visible)
                return;

            state.GameObject.SetActive(visible);
            state.Visible = visible;
        }

        public static void ExternalCleanup(UnitMapIcon icon)
        {
            LineState state;
            if (Lines.TryGetValue(icon, out state))
            {
                if (state != null && state.GameObject != null)
                    Object.Destroy(state.GameObject);

                Lines.Remove(icon);
            }
        }

        public static void ExternalClearAll()
        {
            foreach (var pair in Lines)
            {
                if (pair.Value != null && pair.Value.GameObject != null)
                    Object.Destroy(pair.Value.GameObject);
            }

            Lines.Clear();
            nextUpdateTime = 0f;
        }

        private static LineState CreateLine(Transform parent)
        {
            var go = new GameObject("StrikerTargetLine");
            go.transform.SetParent(parent, false);
            go.transform.SetAsLastSibling();

            var img = go.AddComponent<Image>();
            img.color = new Color(1f, 0f, 0f, 0.8f);
            img.raycastTarget = false;

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);

            // A freshly created GameObject is active. Track that accurately so the first
            // SetVisible(false) call can actually hide it if needed.
            return new LineState
            {
                GameObject = go,
                Image = img,
                Rect = rect,
                Visible = true,
                HasColor = false
            };
        }
    }

    [HarmonyPatch(typeof(UnitMapIcon), "OnRemoveIcon")]
    public class UnitMapIcon_OnRemoveIcon_Patch
    {
        public static void Prefix(UnitMapIcon __instance)
        {
            DynamicMap_Update_Patch.ExternalCleanup(__instance);
        }
    }

    [HarmonyPatch]
    public class DynamicMapPatches
    {
        [HarmonyPatch(typeof(DynamicMap), nameof(DynamicMap.OnEnable))]
        [HarmonyPostfix]
        private static void OnMapEnablePostfix()
        {
            SLineMod.MapExists = true;
        }

        [HarmonyPatch(typeof(DynamicMap), nameof(DynamicMap.OnDestroy))]
        [HarmonyPostfix]
        private static void OnMapDestroyPostfix()
        {
            SLineMod.MapExists = false;
            DynamicMap_Update_Patch.ExternalClearAll();
        }
    }
}
