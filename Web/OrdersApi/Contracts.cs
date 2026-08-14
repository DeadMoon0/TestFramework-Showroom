namespace OrdersApi;

// ══════════════════════════════════════════════════════════════════════════════
//  SPECIMEN UNDER OBSERVATION - CONTRACT SURFACE
//
//  These are the shapes the application shows the outside world. The test suite
//  is not permitted to know anything else about it, which is the entire point of
//  keeping it in its own project where nobody can reach in and reassure themselves.
// ══════════════════════════════════════════════════════════════════════════════

/// <summary>An order as the API hands it back.</summary>
public sealed record Order(int Id, string Name, int Quantity)
{
    /// <summary>What the pricing dependency said, or <c>unpriced</c> when nobody was listening.</summary>
    public string PricingStatus { get; init; } = "unpriced";

    /// <summary>What the pricing dependency charged, in whole currency units.</summary>
    public decimal Total { get; init; }
}

/// <summary>An order as a caller submits it, before the database has an opinion about the key.</summary>
public sealed record CreateOrder(string Name, int Quantity);
