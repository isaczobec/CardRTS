using System.Collections;
using System.Collections.Generic;
using System.Xml.Serialization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DevConsole : Singleton<DevConsole>
{

    [Header("References")]
    [SerializeField] private GameObject scrollViewContent;
    [SerializeField] private GameObject InitialMessage;

    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private Scrollbar scrollbar;

    [SerializeField] private TMP_InputField inputField;

    [SerializeField] private GameObject[] toggleOnOpen;


    [Header("Console Settings")]
    [SerializeField] private int maxConsoleMessages = 200; // Maximum number of messages to keep in the console

    private bool _isOpen = false;
    private List<string> _logMessages = new List<string>();

    private bool showUnityInfo = true;
    private bool showUnityWarnings = true;
    private bool showUnityErrors = true;

    private bool showUnityInfoStackTrace = false;
    private bool showUnityWarningsStackTrace = false;
    private bool showUnityErrorsStackTrace = false;

    /// <summary>
    /// A dictionary of commands that can be executed in the console.
    /// /// The key is the command name, and the value is the DevCommand object.
    /// </summary>
    private Dictionary<string, DevCommand> _commands = new Dictionary<string, DevCommand>();

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    protected override void Awake()
    {
        base.Awake();

        // subscribe to the input field's end edit event (Enter pressed)
        ListenToCommandExecuted();

        // subscribe to the Unity log messages
        ListenToAndPrintUnityLogs();

        if (_isOpen)
            ShowConsole();
        else
            HideConsole();

        // register the clear command
        RegisterCommand(
            new DevCommand(
                "clear",
                "Clears the console.",
                (info) =>
                {
                    ClearConsole();
                    return new DevCommandResult(DevCommandResultType.Success, "");
                })
            );

        // register the help command
        RegisterCommand(
            new DevCommand(
                "help",
                "Lists all available commands or the description of a specific command.",
                (info) =>
                {
                    if (info.positionalArgs.Length > 0)
                    {
                        // if a command name is provided, show its description
                        string commandName = info.positionalArgs[0];
                        if (_commands.TryGetValue(commandName, out DevCommand command))
                        {
                            return new DevCommandResult(DevCommandResultType.Success, $"{command.commandName}: {command.description}");
                        }
                        else
                        {
                            return new DevCommandResult(DevCommandResultType.Error, $"Command '{commandName}' not found.");
                        }
                    }
                    else
                    {
                        // list all commands
                        string message = "Available commands:\n------------------\n";
                        foreach (var cmd in _commands.Values)
                        {
                            message += $"{cmd.commandName}: {cmd.description}\n---\n";
                        }
                        return new DevCommandResult(DevCommandResultType.Success, message);
                    }
                })
            );

        // register the toggle unity logs command
        RegisterCommand(
            new DevCommand(
                "toggle-unity-logs",
                "Toggles the visibility of Unity logs in the console. Syntax: toggle-unity-logs [-info|-warning|-error]. If no flags are provided, all logs are toggled.",
                (info) =>
                {

                    if (info.flagArgs.Length == 0)
                    {
                        showUnityInfo = !showUnityInfo;
                        showUnityWarnings = !showUnityWarnings;
                        showUnityErrors = !showUnityErrors;
                    }

                    foreach (var flag in info.flagArgs)
                    {
                        switch (flag)
                        {
                            case "info":
                                showUnityInfo = !showUnityInfo;
                                break;
                            case "warning":
                                showUnityWarnings = !showUnityWarnings;
                                break;
                            case "error":
                                showUnityErrors = !showUnityErrors;
                                break;
                            default:
                                return new DevCommandResult(DevCommandResultType.Error, $"Unknown flag '{flag}'. Use 'info', 'warning', or 'error'.");
                        }
                    }

                    return new DevCommandResult(DevCommandResultType.Success, $"Unity logs toggled: Info={showUnityInfo}, Warnings={showUnityWarnings}, Errors={showUnityErrors}");
                })
            );

        // register the toggle stack traces command
        RegisterCommand(
            new DevCommand(
                "toggle-stack-traces",
                "Toggles the visibility of stack traces in Unity logs. Syntax: toggle-stack-traces [-info|-warning|-error]. If no flags are provided, all stack traces are toggled.",
                (info) =>
                {

                    if (info.flagArgs.Length == 0)
                    {
                        showUnityInfoStackTrace = !showUnityInfoStackTrace;
                        showUnityWarningsStackTrace = !showUnityWarningsStackTrace;
                        showUnityErrorsStackTrace = !showUnityErrorsStackTrace;
                    }

                    foreach (var flag in info.flagArgs)
                    {
                        switch (flag)
                        {
                            case "info":
                                showUnityInfoStackTrace = !showUnityInfoStackTrace;
                                break;
                            case "warning":
                                showUnityWarningsStackTrace = !showUnityWarningsStackTrace;
                                break;
                            case "error":
                                showUnityErrorsStackTrace = !showUnityErrorsStackTrace;
                                break;
                            default:
                                return new DevCommandResult(DevCommandResultType.Error, $"Unknown flag '{flag}'. Use 'info', 'warning', or 'error'.");
                        }
                    }

                    return new DevCommandResult(DevCommandResultType.Success, $"Stack traces toggled: Info={showUnityInfoStackTrace}, Warnings={showUnityWarningsStackTrace}, Errors={showUnityErrorsStackTrace}");
                })
            );
            
        RegisterCommand(new DevCommand(
            "maxmessages",
            "Sets the max amount of messages. Usage: maxmessages <number>",
            (info) =>
            {
                if (info.positionalArgs.Length < 1)
                {
                    return new DevCommandResult(DevCommandResultType.Error, "Please provide a number.");
                }
                if (int.TryParse(info.positionalArgs[0], out int newMax))
                {

                    if (newMax <= 0)
                    {
                        return new DevCommandResult(DevCommandResultType.Error, "Number must be greater than 0.");
                    }   
                    maxConsoleMessages = newMax;
                    return new DevCommandResult(DevCommandResultType.Success, $"Max messages set to {maxConsoleMessages}.");
                }
                else
                {
                    return new DevCommandResult(DevCommandResultType.Error, "Invalid number format.");
                }
            })
        );
    }

    // Update is called once per frame
    void Update()
    {

        // toggle the console with the F1 key
        if (Input.GetKeyDown(KeyCode.F1))
        {
            if (_isOpen)
            {
                HideConsole();
            }
            else
            {
                ShowConsole();
            }
        }

        // if the console is open, check for input
        if (_isOpen)
        {
            // check for escape key to close the console
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                HideConsole();
            }
        }
    }

    private void ListenToCommandExecuted()
    {
        inputField.onEndEdit.AddListener((value) =>
        {
            // Only execute if Enter was pressed (not if focus lost)
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                ExecuteCommandInput(value);
                inputField.text = string.Empty;
                inputField.ActivateInputField(); // keep focus
            }
        });
    }

    private void ShowConsole()
    {
        if (!_isOpen)
        {
            _isOpen = true;
            // enable the console UI elements
            foreach (GameObject toggle in toggleOnOpen)
            {
                toggle.SetActive(true);
            }
            inputField.ActivateInputField(); // focus the input field
        }
    }

    private void HideConsole()
    {
        if (_isOpen)
        {
            _isOpen = false;
            // disable the console UI elements
            foreach (GameObject toggle in toggleOnOpen)
            {
                toggle.SetActive(false);
            }
            inputField.DeactivateInputField(); // remove focus from the input field
        }
    }



    private void CreateMessage(string message)
    {
        _logMessages.Add(message);

        // Remove oldest messages if over the limit
        while (_logMessages.Count > maxConsoleMessages)
        {
            _logMessages.RemoveAt(0);
            // Also destroy the corresponding UI object (skip InitialMessage)

            foreach (Transform child in scrollViewContent.transform)
            {
                if (child.gameObject != InitialMessage)
                {
                    Destroy(child.gameObject);
                    break;
                }
            }
        }

        // create a new message object in the logger and set the content of its TMPro compenent
        GameObject messageObject = Instantiate(InitialMessage, scrollViewContent.transform);
        TMP_Text text = messageObject.GetComponent<TMP_Text>();
        text.text = message;

        // Wait one frame so the layout system rebuilds Content height before scrolling
        StartCoroutine(ScrollToBottomNextFrame());
    }

    private IEnumerator ScrollToBottomNextFrame()
    {
        yield return null;
        Canvas.ForceUpdateCanvases();
        scrollRect.verticalNormalizedPosition = 0f;
        scrollbar.value = 0f;
    }

    /// <summary>
    /// Registers a command to the console.
    /// If a command with the same name already exists, it will not be registered again and an error message will be logged.
    /// </summary>
    /// <param name="command"></param>
    public static void RegisterCommand(DevCommand command)
    {
        if (!instance._commands.ContainsKey(command.commandName))
        {
            instance._commands.Add(command.commandName, command);
        }
        else
        {
            instance.CreateMessage($"<color=red>Command '{command.commandName}' is already registered.</color>");
        }
    }

    /// <summary>
    /// Registers a command to the console with a name, description, and function.
    /// If a command with the same name already exists, it will not be registered again and an error message will be logged.
    /// </summary>
    public static void RegisterCommand(string commandName, string description, DevCommandDelegate commandFunction)
    {
        DevCommand command = new DevCommand(commandName, description, commandFunction);
        RegisterCommand(command);
    }

    public void ExecuteCommandInput(string commandLine, bool echoCommand = true)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return;
        }

        if (echoCommand)
        {
            // log the command input
            CreateMessage($"<color=blue>{commandLine}</color>");
        }

        DevCommandInfo commandInfo = new DevCommandInfo(commandLine);
        // try to get the command from the dictionary
        if (_commands.TryGetValue(commandInfo.commandName, out DevCommand command))
        {
            DevCommandResult result = command.commandFunction(new DevCommandInfo(commandLine));
            // execute the command
            // log the result

            if (commandLine == "generateworld")
            {
                Debug.Log("Called with generateworld, returning early to see if we can prevent freeze!");
                return;
            }

            if (result.message != null && result.message.Length > 0)
            {
                if (result.resultType == DevCommandResultType.Success)
                {
                    LogInfo(result.message);
                }
                else if (result.resultType == DevCommandResultType.Warning)
                {
                    LogWarning(result.message);
                }
                else if (result.resultType == DevCommandResultType.Error)
                {
                    LogError(result.message);
                }
            }
        }
        else
        {
            CreateMessage($"<color=red>Command '{commandLine}' not found.</color>");
        }
    }


    public static void LogInfo(string message)
    {
        // log message with info style
        instance.CreateMessage(message);
    }

    public static void LogWarning(string message)
    {
        // log message with warning style
        instance.CreateMessage($"<color=yellow>{message}</color>");
    }

    public static void LogError(string message)
    {
        // log message with error style
        instance.CreateMessage($"<color=red>{message}</color>");
    }


    /// <summary>
    /// Listens to Unity's log messages and prints them to the console.
    /// This will also print the stack trace if the corresponding toggle is enabled.
    /// </summary>
    private void ListenToAndPrintUnityLogs()
    {
        Application.logMessageReceived += (string condition, string stackTrace, LogType type) =>
        {
            if (type == LogType.Log && showUnityInfo)
            {
                LogInfo($"[U] {condition}");
            }
            else if (type == LogType.Warning && showUnityWarnings)
            {
                LogWarning($"[U] {condition}");
            }
            else if (type == LogType.Error || type == LogType.Exception)
            {
                if (showUnityErrors)
                {
                    LogError($"[U] {condition}");
                }
            }

            if (type == LogType.Log && showUnityInfoStackTrace)
            {
                LogInfo($"<i>{stackTrace}</i>");
            }
            else if (type == LogType.Warning && showUnityWarningsStackTrace)
            {
                LogWarning($"<i>{stackTrace}</i>");
            }
            else if ((type == LogType.Error || type == LogType.Exception) && showUnityErrorsStackTrace)
            {
                LogError($"<i>{stackTrace}</i>");
            }
        };
    }

    private void ClearConsole()
    {
        // clear all but the initial message
        _logMessages.Clear();
        foreach (Transform child in scrollViewContent.transform)
        {
            if (child.gameObject == InitialMessage)
            {
                continue; // keep the initial message
            }
            Destroy(child.gameObject);
        }
    }
    
}
