using System.Diagnostics;

namespace Gracious;

internal static class RetryLoop
{
    public static async ValueTask RunUntilTimeout(Action callback, TimeSpan timeout, TimeSpan delay)
    {
        Stopwatch sw = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                callback();
                return;
            }
            catch when (sw.Elapsed < timeout)
            {
                await Task.Delay(delay);
            }
        }
    }
}
