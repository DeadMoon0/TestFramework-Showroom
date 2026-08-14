using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using OrdersApi;

// ══════════════════════════════════════════════════════════════════════════════
//  SPECIMEN UNDER OBSERVATION - ORDER INTAKE SERVICE
//
//  A small application that does the two things every application does: it writes
//  to a database, and it calls somebody else. Both halves are observable from the
//  outside, which is fortunate, because from the outside is where the tests live
//  and they have been very clear about not wanting to come in.
//
//  Nothing in this project references the test framework. It does not know it is
//  being watched. Please do not tell it.
// ══════════════════════════════════════════════════════════════════════════════

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
WebApplication app = builder.Build();

// Resolved per request rather than at startup, so the health endpoint keeps answering
// even when the database is absent. A service that reports itself dead because a
// dependency is missing has confused two entirely different questions.
string ConnectionString() => app.Configuration.GetConnectionString("Orders")
    ?? throw new InvalidOperationException("The connection string 'Orders' is not configured.");

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapGet("/api/orders", async () =>
{
    List<Order> orders = [];

    await using SqlConnection connection = new(ConnectionString());
    await connection.OpenAsync();
    await using SqlCommand command = new("SELECT [Id], [Name], [Quantity], [Total] FROM [Orders] ORDER BY [Id];", connection);
    await using SqlDataReader reader = await command.ExecuteReaderAsync();

    while (await reader.ReadAsync())
        orders.Add(new Order(reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2)) { Total = reader.GetDecimal(3) });

    return Results.Ok(orders);
});

app.MapPost("/api/orders", async (CreateOrder order) =>
{
    if (string.IsNullOrWhiteSpace(order.Name))
        return Results.BadRequest(new { error = "Name is required." });

    // Step one: ask somebody else what this costs. The answer arrives over HTTP,
    // which means it can be observed, recorded, and later held against us.
    (string status, decimal total) = await PriceAsync(order.Quantity);

    await using SqlConnection connection = new(ConnectionString());
    await connection.OpenAsync();
    await using SqlCommand command = new(
        "INSERT INTO [Orders] ([Name], [Quantity], [Total]) VALUES (@name, @quantity, @total); SELECT CAST(SCOPE_IDENTITY() AS INT);",
        connection);

    command.Parameters.Add("@name", SqlDbType.NVarChar, 200).Value = order.Name;
    command.Parameters.Add("@quantity", SqlDbType.Int).Value = order.Quantity;
    command.Parameters.Add("@total", SqlDbType.Decimal).Value = total;

    object? assignedId = await command.ExecuteScalarAsync();
    if (assignedId is not int id)
        return Results.Problem("The database declined to assign an identity, which it has never done before and will not explain.");

    return Results.Created(
        $"/api/orders/{id.ToString(CultureInfo.InvariantCulture)}",
        new Order(id, order.Name, order.Quantity) { PricingStatus = status, Total = total });
});

// The outbound dependency. Optional on purpose: an unconfigured integration should
// degrade quietly rather than take the whole service down out of solidarity.
async Task<(string Status, decimal Total)> PriceAsync(int quantity)
{
    string? pricingBaseUrl = app.Configuration["Services:Pricing:BaseUrl"];
    if (string.IsNullOrWhiteSpace(pricingBaseUrl))
        return ("unpriced", 0m);

    using HttpClient client = new() { BaseAddress = new Uri(pricingBaseUrl, UriKind.Absolute) };
    using HttpResponseMessage response = await client.PostAsJsonAsync("api/quotes", new { quantity });

    if (!response.IsSuccessStatusCode)
        return ($"declined:{(int)response.StatusCode}", 0m);

    Quote? quote = await response.Content.ReadFromJsonAsync<Quote>();
    return (quote?.Status ?? "unknown", quote?.Total ?? 0m);
}

await app.RunAsync();

internal sealed record Quote(string Status, decimal Total);
