namespace NuLua;

/// <summary>VM-wide memory accounting and optional allocator limits.</summary>
public interface ILuaMemoryControl
{
    long UsedBytes { get; }
    bool SupportsLimit { get; }

    /// <summary>Sets an allocator limit and returns the previous limit. Zero means unlimited.</summary>
    long SetLimit(long bytes);
}
