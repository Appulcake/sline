using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;

namespace SLine
{
    [BepInPlugin("com.sline", "SLine Target Mod", "1.0.0")]
    public class SLineMod : BaseUnityPlugin
    {
        private void Awake()
        {
            var harmony = new Harmony("com.sline");
            harmony.PatchAll();
            Logger.LogInfo("SLine Mod Initialized");
        }
    }

    [HarmonyPatch(typeof(DynamicMap), "Update")]
    public class DynamicMap_Update_Patch
    {
        private static Dictionary<UnitMapIcon, GameObject> lines = new Dictionary<UnitMapIcon, GameObject>();

        private static FieldInfo missileTargetField = typeof(Missile).GetField("target", BindingFlags.Instance | BindingFlags.NonPublic);

        public static void Postfix(DynamicMap __instance)
        {
            try
            {
                var icons = __instance.mapIcons;
                if (icons == null) return;

                HashSet<UnitMapIcon> updatedIcons = new HashSet<UnitMapIcon>();

                foreach (var baseIcon in icons)
                {
                    var icon = baseIcon as UnitMapIcon;
                    if (icon == null || icon.unit == null || !icon.gameObject.activeInHierarchy) continue;

                    Unit target = null;
                    if (icon.unit is Missile missile)
                    {
                        if (missileTargetField != null)
                            target = (Unit)missileTargetField.GetValue(missile);
                    }
                    else if (icon.unit is Aircraft aircraft && aircraft.weaponManager != null)
                    {
                        var targets = aircraft.weaponManager.GetTargetList();
                        if (targets != null && targets.Count > 0)
                        {
                            target = targets[0];
                        }
                    }

                    if (target != null)
                    {
                        UnitMapIcon targetIcon = null;
                        if (DynamicMap.TryGetMapIcon(target, out targetIcon) && targetIcon != null && targetIcon.gameObject.activeInHierarchy)
                        {
                            UpdateLine(icon, targetIcon, lines, target);
                            updatedIcons.Add(icon);
                            continue;
                        }
                    }
                    
                    HideLine(icon, lines);
                }

                List<UnitMapIcon> toRemove = new List<UnitMapIcon>();
                foreach (var pair in lines)
                {
                    if (!updatedIcons.Contains(pair.Key))
                    {
                        if (pair.Value != null) pair.Value.SetActive(false);
                        if (pair.Key == null || !pair.Key.gameObject.activeInHierarchy) toRemove.Add(pair.Key);
                    }
                }
                foreach (var icon in toRemove) lines.Remove(icon);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[SLine Mod] Error in DynamicMap_Update_Patch: " + e.Message + "\n" + e.StackTrace);
            }
        }

        private static void UpdateLine(UnitMapIcon strikerIcon, UnitMapIcon targetIcon, Dictionary<UnitMapIcon, GameObject> lines, Unit target)
        {
            if (!lines.TryGetValue(strikerIcon, out var lineObj) || lineObj == null)
            {
                lineObj = CreateLine(strikerIcon.transform.parent);
                lines[strikerIcon] = lineObj;
            }

            lineObj.SetActive(true);
            var img = lineObj.GetComponent<Image>();
            if (strikerIcon.unit is Aircraft)
            {
                img.color = new Color(1f, 0f, 1f, 0.8f); // Magenta
            }
            else
            {
                if (target is Aircraft)
                {
                    img.color = new Color(0f, 1f, 1f, 0.8f); // Cyan
                }
                else if (target is Ship)
                {
                    img.color = new Color(1f, 0f, 0f, 0.8f); // Red
                }
                else if (target is Missile)
                {
                    img.color = new Color(1f, 1f, 1f, 0.8f); // White
                }
                else 
                {
                    img.color = new Color(1f, 1f, 0f, 0.8f); // Yellow (Ground)
                }
            }

            var rect = lineObj.GetComponent<RectTransform>();
            
            // Positions are in local space of the icon layer
            Vector3 startPos = strikerIcon.transform.localPosition;
            Vector3 endPos = targetIcon.transform.localPosition;
            
            Vector3 diff = endPos - startPos;
            float distance = diff.magnitude;
            
            if (distance < 5f)
            {
                lineObj.SetActive(false);
                return;
            }

            float angle = Mathf.Atan2(diff.y, diff.x) * Mathf.Rad2Deg;

            rect.localPosition = startPos;
            rect.localRotation = Quaternion.Euler(0, 0, angle);
            rect.sizeDelta = new Vector2(distance, 0.1f);
        }

        private static void HideLine(UnitMapIcon strikerIcon, Dictionary<UnitMapIcon, GameObject> lines)
        {
            if (lines.TryGetValue(strikerIcon, out var lineObj) && lineObj != null)
            {
                lineObj.SetActive(false);
            }
        }

        public static void ExternalCleanup(UnitMapIcon icon)
        {
            if (lines.TryGetValue(icon, out var lineObj) && lineObj != null)
            {
                Object.Destroy(lineObj);
            }
            lines.Remove(icon);
        }

        private static GameObject CreateLine(Transform parent)
        {
            var go = new GameObject("StrikerTargetLine");
            go.transform.SetParent(parent, false);
            go.transform.SetAsLastSibling(); 
            
            var img = go.AddComponent<Image>();
            img.color = new Color(1f, 0f, 0f, 0.8f);
            img.raycastTarget = false; 
            
            var rect = go.GetComponent<RectTransform>();
            // Use center anchor so that localPosition matches the Map icons' localPosition
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0, 0.5f);
            
            return go;
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
}
