using System.Diagnostics;

namespace Gracious;

internal static partial class ProcessExtensions
{
    public static bool TryKill(this Process @this)
    {
        try
        {
            @this.Kill();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
