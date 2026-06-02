using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The command name and args it is called with.
/// </summary>
public class DevCommandInfo
{
    private string _commandName;
    private string[] _positionalArgs;
    private string[] _flagArgs;
    private Dictionary<string, string> _keyWordArgs = new Dictionary<string, string>();
    public string commandName { get { return _commandName; } }
    public string[] positionalArgs { get { return _positionalArgs; } }
    public string[] flagArgs { get { return _flagArgs; } }
    public Dictionary<string, string> keyWordArgs { get { return _keyWordArgs; } }

    /// <summary>
    /// Constructs a DevCommandInfo from a raw command string.
    /// /// The command string should be formatted as "commandName arg1 arg2 -flag1 -flag2".
    /// </summary>
    /// <param name="rawCommand"></param>
    public DevCommandInfo(string rawCommand)
    {
        // Split the command into parts
        string[] parts = rawCommand.Split(' ');

        // The first part is the command name
        _commandName = parts[0];

        // The rest are either positional or flag arguments
        List<string> positionalArgsList = new List<string>();
        List<string> flagArgsList = new List<string>();
        Dictionary<string, string> keyWordArgsDict = new Dictionary<string, string>();

        for (int i = 1; i < parts.Length; i++)
        {
            if (parts[i].StartsWith("-"))
            {
                // remove the leading '-' from flag arguments
                parts[i] = parts[i].TrimStart('-');
                flagArgsList.Add(parts[i]);
            }
            else if (parts[i].Contains("="))
            {
                // handle keyword arguments like "key=value"
                string[] keyValue = parts[i].Split('=');
                if (keyValue.Length == 2)
                {
                    keyWordArgsDict[keyValue[0]] = keyValue[1];
                }
                else
                {
                    // If it doesn't split into two parts, treat it as a positional argument
                    positionalArgsList.Add(parts[i]);
                }
            }
            else
            {
                positionalArgsList.Add(parts[i]);
            }
        }

        // Convert lists to arrays
        _positionalArgs = positionalArgsList.ToArray();
        _flagArgs = flagArgsList.ToArray();
        _keyWordArgs = keyWordArgsDict;
    }
}

public enum DevCommandResultType
{
    Success,
    Error,
    Warning,
}

public class DevCommandResult
{
    public DevCommandResultType resultType { get; private set; }
    public string message { get; private set; }

    public DevCommandResult(DevCommandResultType resultType, string message = "")
    {
        this.resultType = resultType;
        this.message = message;
    }

    public static DevCommandResult Success(string message = "")
    {
        return new DevCommandResult(DevCommandResultType.Success, message);
    }

    public static DevCommandResult Error(string message = "")
    {
        return new DevCommandResult(DevCommandResultType.Error, message);
    }

    public static DevCommandResult Warning(string message = "")
    {
        return new DevCommandResult(DevCommandResultType.Warning, message);
    }
}


public delegate DevCommandResult DevCommandDelegate(DevCommandInfo commandInfo);
public class DevCommand
{
    public string commandName { get; private set; }

    public string description { get; private set; }

    /// <summary>
    /// The function that will be called when the command is executed.
    /// </summary>
    public DevCommandDelegate commandFunction { get; private set; }

    public DevCommand(string commandName, string description, DevCommandDelegate commandFunction)
    {
        this.commandName = commandName;
        this.description = description;
        this.commandFunction = commandFunction;
    }
}