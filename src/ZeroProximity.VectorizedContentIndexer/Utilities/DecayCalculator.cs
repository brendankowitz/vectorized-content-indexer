namespace ZeroProximity.VectorizedContentIndexer.Utilities;

/// <summary>
/// Calculates temporal decay for content relevance using exponential decay formula.
/// </summary>
/// <remarks>
/// <para>
/// Temporal decay models how information becomes less relevant over time.
/// The exponential decay formula ensures that:
/// <list type="bullet">
///   <item><description>Recent content has high relevance</description></item>
///   <item><description>Older content gradually loses relevance</description></item>
///   <item><description>Very old content approaches zero relevance</description></item>
/// </list>
/// </para>
/// <para>
/// Formula: decay_factor = 0.5^(days_since_reinforced / half_life)
/// </para>
/// <para>
/// Example with 90-day half-life:
/// <list type="bullet">
///   <item><description>0 days: factor = 1.0 (100%)</description></item>
///   <item><description>90 days: factor = 0.5 (50%)</description></item>
///   <item><description>180 days: factor = 0.25 (25%)</description></item>
///   <item><description>365 days: factor = ~0.06 (6%)</description></item>
/// </list>
/// </para>
/// </remarks>
public static class DecayCalculator
{
    /// <summary>
    /// Default half-life in days (90 days).
    /// </summary>
    /// <remarks>
    /// At 90 days, content retains 50% of its original relevance.
    /// This value is suitable for most knowledge management scenarios.
    /// </remarks>
    public const double DefaultHalfLifeDays = 90.0;

    /// <summary>
    /// Calculates the decay factor based on time since last reinforcement.
    /// </summary>
    /// <param name="lastReinforced">When the content was last reinforced or updated.</param>
    /// <param name="halfLifeDays">Half-life in days (default: 90).</param>
    /// <returns>Decay factor between 0 and 1, where 1 means no decay.</returns>
    /// <exception cref="ArgumentException">Thrown when half-life is not positive.</exception>
    /// <remarks>
    /// Formula: 0.5^(days_since_reinforced / half_life)
    /// </remarks>
    public static double CalculateDecayFactor(DateTime lastReinforced, double halfLifeDays = DefaultHalfLifeDays)
    {
        if (halfLifeDays <= 0)
        {
            throw new ArgumentException("Half-life must be positive", nameof(halfLifeDays));
        }

        var daysSinceReinforced = (DateTime.UtcNow - lastReinforced).TotalDays;

        // Ensure we don't have future dates (clock skew protection)
        if (daysSinceReinforced < 0)
        {
            daysSinceReinforced = 0;
        }

        return Math.Pow(0.5, daysSinceReinforced / halfLifeDays);
    }

    /// <summary>
    /// Applies decay to a base score.
    /// </summary>
    /// <param name="baseScore">Original score.</param>
    /// <param name="decayFactor">Decay factor from <see cref="CalculateDecayFactor"/>.</param>
    /// <returns>Score with decay applied.</returns>
    public static double ApplyDecay(double baseScore, double decayFactor)
    {
        return baseScore * decayFactor;
    }

    /// <summary>
    /// Applies decay to a base score using last reinforced timestamp.
    /// </summary>
    /// <param name="baseScore">Original score.</param>
    /// <param name="lastReinforced">When the content was last reinforced.</param>
    /// <param name="halfLifeDays">Half-life in days (default: 90).</param>
    /// <returns>Score with decay applied.</returns>
    public static double ApplyDecay(double baseScore, DateTime lastReinforced, double halfLifeDays = DefaultHalfLifeDays)
    {
        var decayFactor = CalculateDecayFactor(lastReinforced, halfLifeDays);
        return ApplyDecay(baseScore, decayFactor);
    }

    /// <summary>
    /// Determines decay status based on decay factor.
    /// </summary>
    /// <param name="decayFactor">Decay factor between 0 and 1.</param>
    /// <returns>Status string indicating content freshness.</returns>
    /// <remarks>
    /// Status thresholds:
    /// <list type="bullet">
    ///   <item><description>Fresh: greater than 0.75 (less than ~38 days old)</description></item>
    ///   <item><description>Good: greater than 0.50 (less than ~90 days old)</description></item>
    ///   <item><description>Aging: greater than 0.25 (less than ~180 days old)</description></item>
    ///   <item><description>Decaying: greater than 0.10 (less than ~300 days old)</description></item>
    ///   <item><description>Expiring: 0.10 or less (more than ~300 days old)</description></item>
    /// </list>
    /// </remarks>
    public static string GetDecayStatus(double decayFactor)
    {
        return decayFactor switch
        {
            > 0.75 => "Fresh",
            > 0.50 => "Good",
            > 0.25 => "Aging",
            > 0.10 => "Decaying",
            _ => "Expiring"
        };
    }

    /// <summary>
    /// Checks if content should be considered expired.
    /// </summary>
    /// <param name="decayFactor">Decay factor between 0 and 1.</param>
    /// <param name="threshold">Expiration threshold (default: 0.05).</param>
    /// <returns>True if content is expired (below threshold).</returns>
    public static bool IsExpired(double decayFactor, double threshold = 0.05)
    {
        return decayFactor < threshold;
    }
}
