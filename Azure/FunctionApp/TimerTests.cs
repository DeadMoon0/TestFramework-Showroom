using Microsoft.Azure.Functions.Worker;

namespace FunctionApp;

public class TimerTests
{
    private static object _lock = new object();
    public static int TimerInvocationCount { get; set; }

    [Function("TimerTest")]
    public void Run([TimerTrigger("0 */5 * * * *")] TimerInfo myTimer)
    {
        lock (_lock) TimerInvocationCount++;
    }
}