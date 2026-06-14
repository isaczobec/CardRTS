using System;

[Flags]
public enum NetworkRole
{
    Standalone = 0,
    Server     = 1 << 0,
    Client     = 1 << 1,
    Host       = Server | Client,
}
