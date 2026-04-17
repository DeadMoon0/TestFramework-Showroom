using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace FunctionApp;

public class HttpTests
{
    [Function("HttpTest")]
    public IActionResult Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req)
    {
        return new OkObjectResult("The HTTP trigger function executed successfully.\r\nTimerInvocationCount: " + TimerTests.TimerInvocationCount);
    }

    [Function("HttpEchoTest")]
    public async Task<IActionResult> Echo(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req)
    {
        using StreamReader reader = new(req.Body);
        string body = await reader.ReadToEndAsync();
        string contentType = req.ContentType ?? string.Empty;
        string testHeader = req.Headers.TryGetValue("x-test", out var singleHeader)
            ? singleHeader.ToString()
            : string.Empty;
        string valueHeader = req.Headers.TryGetValue("x-values", out var multiHeader)
            ? string.Join(",", multiHeader.ToArray())
            : string.Empty;

        return new OkObjectResult($"Method={req.Method};ContentType={contentType};XTest={testHeader};XValues={valueHeader};Body={body}");
    }

    [Function("HttpAcceptedTest")]
    public IActionResult Accepted([HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequest req)
    {
        return new ObjectResult("Accepted")
        {
            StatusCode = StatusCodes.Status202Accepted,
        };
    }
}