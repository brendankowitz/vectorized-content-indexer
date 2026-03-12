namespace ZeroProximity.VectorizedContentIndexer.Tests.Utilities;

/// <summary>
/// Unit tests for <see cref="DecayCalculator"/>.
/// </summary>
public class DecayCalculatorTests
{
    [Fact]
    public void CalculateDecayFactor_JustNow_ReturnsOne()
    {
        // Arrange
        var lastReinforced = DateTime.UtcNow;

        // Act
        var factor = DecayCalculator.CalculateDecayFactor(lastReinforced);

        // Assert
        factor.Should().BeApproximately(1.0, 0.01);
    }

    [Fact]
    public void CalculateDecayFactor_AtHalfLife_ReturnsHalf()
    {
        // Arrange
        var halfLife = DecayCalculator.DefaultHalfLifeDays; // 90 days
        var lastReinforced = DateTime.UtcNow.AddDays(-halfLife);

        // Act
        var factor = DecayCalculator.CalculateDecayFactor(lastReinforced);

        // Assert
        factor.Should().BeApproximately(0.5, 0.01);
    }

    [Fact]
    public void CalculateDecayFactor_TwoHalfLives_ReturnsQuarter()
    {
        // Arrange
        var halfLife = DecayCalculator.DefaultHalfLifeDays; // 90 days
        var lastReinforced = DateTime.UtcNow.AddDays(-halfLife * 2);

        // Act
        var factor = DecayCalculator.CalculateDecayFactor(lastReinforced);

        // Assert
        factor.Should().BeApproximately(0.25, 0.01);
    }

    [Fact]
    public void CalculateDecayFactor_FutureDate_ReturnsOne()
    {
        // Arrange - simulate clock skew with future date
        var lastReinforced = DateTime.UtcNow.AddDays(10);

        // Act
        var factor = DecayCalculator.CalculateDecayFactor(lastReinforced);

        // Assert - should clamp to 1.0 for future dates
        factor.Should().BeApproximately(1.0, 0.001);
    }

    [Fact]
    public void CalculateDecayFactor_CustomHalfLife_Works()
    {
        // Arrange
        var customHalfLife = 30.0; // 30 days
        var lastReinforced = DateTime.UtcNow.AddDays(-30);

        // Act
        var factor = DecayCalculator.CalculateDecayFactor(lastReinforced, customHalfLife);

        // Assert
        factor.Should().BeApproximately(0.5, 0.01);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void CalculateDecayFactor_InvalidHalfLife_ThrowsArgumentException(double halfLife)
    {
        // Arrange
        var lastReinforced = DateTime.UtcNow;

        // Act & Assert
        var act = () => DecayCalculator.CalculateDecayFactor(lastReinforced, halfLife);
        act.Should().Throw<ArgumentException>()
            .WithParameterName("halfLifeDays");
    }

    [Fact]
    public void ApplyDecay_WithFactor_MultipliesCorrectly()
    {
        // Arrange
        var baseScore = 100.0;
        var decayFactor = 0.5;

        // Act
        var result = DecayCalculator.ApplyDecay(baseScore, decayFactor);

        // Assert
        result.Should().Be(50.0);
    }

    [Fact]
    public void ApplyDecay_WithTimestamp_CalculatesCorrectly()
    {
        // Arrange
        var baseScore = 100.0;
        var lastReinforced = DateTime.UtcNow.AddDays(-90); // One half-life

        // Act
        var result = DecayCalculator.ApplyDecay(baseScore, lastReinforced);

        // Assert
        result.Should().BeApproximately(50.0, 1.0);
    }

    [Theory]
    [InlineData(1.0, "Fresh")]
    [InlineData(0.9, "Fresh")]
    [InlineData(0.76, "Fresh")]
    [InlineData(0.75, "Good")]
    [InlineData(0.6, "Good")]
    [InlineData(0.51, "Good")]
    [InlineData(0.50, "Aging")]
    [InlineData(0.3, "Aging")]
    [InlineData(0.26, "Aging")]
    [InlineData(0.25, "Decaying")]
    [InlineData(0.15, "Decaying")]
    [InlineData(0.11, "Decaying")]
    [InlineData(0.10, "Expiring")]
    [InlineData(0.05, "Expiring")]
    [InlineData(0.0, "Expiring")]
    public void GetDecayStatus_ReturnsCorrectStatus(double decayFactor, string expectedStatus)
    {
        // Act
        var status = DecayCalculator.GetDecayStatus(decayFactor);

        // Assert
        status.Should().Be(expectedStatus);
    }

    [Theory]
    [InlineData(0.06, 0.05, false)]
    [InlineData(0.05, 0.05, false)]
    [InlineData(0.049, 0.05, true)]
    [InlineData(0.04, 0.05, true)]
    [InlineData(0.0, 0.05, true)]
    public void IsExpired_WithThreshold_ReturnsCorrectResult(double decayFactor, double threshold, bool expectedExpired)
    {
        // Act
        var isExpired = DecayCalculator.IsExpired(decayFactor, threshold);

        // Assert
        isExpired.Should().Be(expectedExpired);
    }

    [Fact]
    public void IsExpired_DefaultThreshold_Works()
    {
        // Default threshold is 0.05
        DecayCalculator.IsExpired(0.06).Should().BeFalse();
        DecayCalculator.IsExpired(0.04).Should().BeTrue();
    }

    [Fact]
    public void DefaultHalfLifeDays_Is90()
    {
        // Assert
        DecayCalculator.DefaultHalfLifeDays.Should().Be(90.0);
    }

    [Fact]
    public void CalculateDecayFactor_VeryOldDate_ApproachesZero()
    {
        // Arrange - 2 years ago
        var lastReinforced = DateTime.UtcNow.AddDays(-730);

        // Act
        var factor = DecayCalculator.CalculateDecayFactor(lastReinforced);

        // Assert - should be very small but not zero
        factor.Should().BeGreaterThan(0);
        factor.Should().BeLessThan(0.01);
    }
}
