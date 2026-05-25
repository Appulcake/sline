using System;
using System.Collections.Generic;
using System.IO;
using Rewired;

namespace SLine
{
    public class ModInputAction
    {
        public string Name { get; set; }
        public Rewired.InputActionType Type { get; set; }
        public int AssignedId { get; set; }

        public ModInputAction(string name, Rewired.InputActionType type)
        {
            Name = name;
            Type = type;
            AssignedId = -1;
        }
    }

    public static class ExtraInputManager
    {
        public static List<ModInputAction> PendingActions = new List<ModInputAction>();
        public static bool RewiredInitialized = false;

        private static string ConfigPath => Path.Combine(Path.GetDirectoryName(typeof(ExtraInputManager).Assembly.Location) ?? "", "ExtraInputActions.json");

        public static void RegisterAction(string actionName, InputActionType type)
        {
            if (PendingActions.Exists(a => a.Name == actionName))
                return;

            PendingActions.Add(new ModInputAction(actionName, type));
            SavePendingActions();
        }

        public static void LoadPendingActions()
        {
            // Statically registered via Plugin / SLineMod Awake
        }

        public static void SavePendingActions()
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("[");
                for (int i = 0; i < PendingActions.Count; i++)
                {
                    var act = PendingActions[i];
                    sb.AppendLine("  {");
                    sb.AppendLine($"    \"Name\": \"{act.Name}\",");
                    sb.AppendLine($"    \"Type\": {(int)act.Type},");
                    sb.AppendLine($"    \"AssignedId\": {act.AssignedId}");
                    sb.Append("  }");
                    if (i < PendingActions.Count - 1)
                        sb.AppendLine(",");
                    else
                        sb.AppendLine();
                }
                sb.AppendLine("]");
                
                string dir = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                
                File.WriteAllText(ConfigPath, sb.ToString());
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[ExtraInputManager] Error saving actions: {ex}");
            }
        }
    }
}
