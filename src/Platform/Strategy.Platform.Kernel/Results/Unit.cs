namespace Strategy.Platform.Kernel.Results;

/// <summary>"No value" for commands that only succeed or fail.</summary>
public readonly record struct Unit
{
    public static readonly Unit Value;
}
