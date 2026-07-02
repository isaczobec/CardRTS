using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class DebugCategory
{

    public string name;
    public bool isEnabled;
}
public class DebugLogger : Singleton<DebugLogger>
{
    [SerializeField] private List<DebugCategory> debugCategories;

    protected override void Awake()
    {
        base.Awake();
    }

    void Start()
    {
        // Register command to toggle debug categories
        DevConsole.RegisterCommand("togglecategory", "Toggles a debug category on or off. Usage: togglecategory [categoryName]",
            (info) =>
            {
                if (info.positionalArgs.Length < 1)
                {
                    return new DevCommandResult(DevCommandResultType.Error, "No category name provided. Usage: togglecategory [categoryName]");
                }
                string categoryName = info.positionalArgs[0];
                bool categoryFound = false;
                foreach (var debugCategory in debugCategories)
                {
                    if (debugCategory.name == categoryName)
                    {
                        debugCategory.isEnabled = !debugCategory.isEnabled; // Toggle the category
                        categoryFound = true;
                        return new DevCommandResult(DevCommandResultType.Success, $"Toggled category '{categoryName}' to {(debugCategory.isEnabled ? "enabled" : "disabled")}.");
                    }
                }
                if (!categoryFound)
                {
                    return new DevCommandResult(DevCommandResultType.Error, $"Category '{categoryName}' not found.");
                }
                return new DevCommandResult(DevCommandResultType.Error, "An unknown error occurred while toggling the category.");
            }
        );

        // Register command to list all debug categories and if they are enabled or disabled
        DevConsole.RegisterCommand("listcategories", "Lists all debug categories and their enabled/disabled status.",
            (info) =>
            {
                if (debugCategories.Count == 0)
                {
                    return new DevCommandResult(DevCommandResultType.Error, "No debug categories found.");
                }
                string result = "Debug Categories:\n";
                foreach (var debugCategory in debugCategories)
                {
                    result += $"{debugCategory.name}: {(debugCategory.isEnabled ? "Enabled" : "Disabled")}\n";
                }
                return new DevCommandResult(DevCommandResultType.Success, result);
            }
        );
    }

    private void _Log(string message, string category = null, LogType logType = LogType.Log)
    {
        if (category != null)
        {
            bool categoryExisted = false;
            foreach (var debugCategory in debugCategories)
            {
                if (debugCategory.name == category )
                {
                    categoryExisted = true;
                    if (!debugCategory.isEnabled)
                        return; // Skip logging if the category is disabled. Only disable known categories.
                }
            }
            if (!categoryExisted)
            {
                debugCategories.Add(new DebugCategory { name = category, isEnabled = true });
            }
        }


        switch (logType)
        {
            case LogType.Log:
                Debug.Log(message);
                break;
            case LogType.Warning:
                Debug.LogWarning(message);
                break;
            case LogType.Error:
                Debug.LogError(message);
                break;
            case LogType.Exception:
                Debug.LogException(new System.Exception(message));
                break;
            default:
                Debug.Log(message);
                break;
        }
    }

    public static void Log(string message, string category = null)
    {
        instance._Log(message, category, LogType.Log);
    }
    public static void LogWarning(string message, string category = null)
    {
        instance._Log(message, category, LogType.Warning);
    }
    public static void LogError(string message, string category = null)
    {
        instance._Log(message, category, LogType.Error);
    }
    public static void LogException(string message, string category = null)
    {
        instance._Log(message, category, LogType.Exception);
    }

}