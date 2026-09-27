namespace WhoCarried.Core;

/// <summary>Block taken off a creature without a hit (Expose): only what it actually had.</summary>
public static class BlockStrip
{
    public static int Removed(decimal amount, int block) => amount <= 0m || block <= 0 ? 0 : (int)Math.Min(amount, block);
}
