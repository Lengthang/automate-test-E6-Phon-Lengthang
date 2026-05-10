using SmartPark.Core.Models;
using SmartPark.Core.Services;
using FsCheck;
using FsCheck.Xunit;

namespace SmartPark.Tests;

public class ParkingFeeCalculatorTests
{
    private readonly ParkingFeeCalculator _calculator = new();

    // ────────────────────────────────────────────────────────────
    //  EXAMPLE TEST — shows the naming convention and AAA pattern.
    //  Delete or keep this; it does not count toward your grade.
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void CalculateFee_ZeroDuration_ReturnsFree()
    {
        // Arrange
        var checkIn = new DateTime(2026, 3, 16, 10, 0, 0);  // Monday
        var checkOut = checkIn; // same time = 0 duration

        // Act
        var result = _calculator.CalculateFee(VehicleType.Car, MembershipTier.Guest, checkIn, checkOut);

        // Assert
        Assert.Equal(0m, result.TotalFee);
    }

    #region Basic Fee Calculation
    // Test basic hourly rates for each vehicle type
    // Consider using [Theory] with [InlineData] for multiple scenarios
    [Fact]
    public void CalculateFee_Motorcycle_2Hours_Returns1000()
    {
        // Arrange
        var checkIn = new DateTime(2026, 3, 16, 10, 0, 0); // Monday, no surcharge
        var checkOut = new DateTime(2026, 3, 16, 12, 0, 0); // 2 hours

        // Act
        var result = _calculator.CalculateFee(
            VehicleType.Motorcycle, MembershipTier.Guest, checkIn, checkOut);

        // Assert
        Assert.Equal(1000m, result.TotalFee);
    }
    
    [Theory]
    [InlineData(VehicleType.Car, 3, 3000)]
    [InlineData(VehicleType.SUV, 1, 1500)]
    public void CalculateFee_BasicRate_ReturnsCorrectFee(VehicleType vehicleType, int hours, decimal expected)
    {
        var checkIn  = new DateTime(2026, 3, 16, 10, 0, 0);
        var checkOut = checkIn.AddHours(hours);

        var result = _calculator.CalculateFee(
            vehicleType, MembershipTier.Guest, checkIn, checkOut);

        Assert.Equal(expected, result.TotalFee);
    }

    #endregion

    #region Grace Period
    // Test the free parking window and its boundaries
    [Theory]
    [InlineData(0)]
    [InlineData(29)]
    [InlineData(30)]
    public void CalculateFee_GracePeriod_ReturnsFree(int minutes)
    {
        var checkIn  = new DateTime(2026, 3, 16, 10, 0, 0);
        var checkOut = checkIn.AddMinutes(minutes);

        var result = _calculator.CalculateFee(VehicleType.Car, MembershipTier.Guest, checkIn, checkOut);

        Assert.Equal(0m, result.TotalFee);
    }
    #endregion

    #region Duration Rounding
    // Test how partial hours are rounded for billing
    [Theory]
    [InlineData(91,  2000)]  // 61 min past grace → ceil(61/60) = 2 hours
    [InlineData(151, 3000)]  // 121 min past grace → ceil(121/60) = 3 hours
    public void CalculateFee_DurationRounding_CeilsToNextHour(int totalMinutes, decimal expected)
    {
        var checkIn  = new DateTime(2026, 3, 16, 10, 0, 0);
        var checkOut = checkIn.AddMinutes(totalMinutes);

        var result = _calculator.CalculateFee(VehicleType.Car, MembershipTier.Guest, checkIn, checkOut);

        Assert.Equal(expected, result.TotalFee);
    }
    #endregion

    #region Daily Cap
    // Test that fees respect maximum daily limits per vehicle type
    [Theory]
    [InlineData(VehicleType.Motorcycle, 10,  4_000)]  // 10h × 500 = 5,000 → capped at 4,000
    [InlineData(VehicleType.Car,        12,  8_000)]  // 12h × 1,000 = 12,000 → capped at 8,000
    [InlineData(VehicleType.SUV,        24, 12_000)]  // 24h × 1,500 = 36,000 → capped at 12,000
    public void CalculateFee_DailyCap_FeeNeverExceedsCap(VehicleType vehicleType, int hours, decimal expected)
    {
        var checkIn  = new DateTime(2026, 3, 16, 8, 0, 0);
        var checkOut = checkIn.AddHours(hours);

        var result = _calculator.CalculateFee(vehicleType, MembershipTier.Guest, checkIn, checkOut);

        Assert.Equal(expected, result.TotalFee);
    }
    #endregion

    #region Overnight Fee
    // Test the flat fee applied for sessions that extend into late hours
    [Fact]
    public void CalculateFee_Overnight_SessionPast10PM_AddsOvernightFee()
    {
        var checkIn  = new DateTime(2026, 3, 16, 20, 0, 0); // 8 PM
        var checkOut = new DateTime(2026, 3, 16, 23, 0, 0); // 11 PM — 3 hours

        var result = _calculator.CalculateFee(VehicleType.Car, MembershipTier.Guest, checkIn, checkOut);

        Assert.Equal(5000m, result.TotalFee); // 3,000 base + 2,000 overnight
    }

    [Fact]
    public void CalculateFee_Overnight_SessionEntirelyBelow10PM_NoOvernightFee()
    {
        var checkIn  = new DateTime(2026, 3, 16, 8, 0, 0);
        var checkOut = new DateTime(2026, 3, 16, 17, 0, 0); // 9 hours → capped at 8,000

        var result = _calculator.CalculateFee(VehicleType.Car, MembershipTier.Guest, checkIn, checkOut);

        Assert.Equal(8000m, result.TotalFee); // no overnight fee
    }
    #endregion

    #region Weekend Surcharge
    // Test the percentage-based surcharge on specific days
    [Theory]
    [InlineData(DayOfWeek.Saturday, 2400)]
    [InlineData(DayOfWeek.Sunday,   2400)]
    public void CalculateFee_WeekendSurcharge_AppliesTwentyPercent(DayOfWeek day, decimal expected)
    {
        // Find the next occurrence of the target day
        var date = new DateTime(2026, 3, 14); // Saturday
        while (date.DayOfWeek != day) date = date.AddDays(1);
        var checkIn  = date.AddHours(10);
        var checkOut = checkIn.AddHours(2);

        var result = _calculator.CalculateFee(VehicleType.Car, MembershipTier.Guest, checkIn, checkOut);

        Assert.Equal(expected, result.TotalFee); // 2,000 + 400 surcharge
    }

    [Fact]
    public void CalculateFee_Weekday_NoSurcharge()
    {
        var checkIn  = new DateTime(2026, 3, 16, 10, 0, 0); // Monday
        var checkOut = checkIn.AddHours(2);

        var result = _calculator.CalculateFee(VehicleType.Car, MembershipTier.Guest, checkIn, checkOut);

        Assert.Equal(2000m, result.TotalFee);
    }
    #endregion

    #region Holiday Surcharge
    // Test holiday pricing and its interaction with weekend pricing
    [Fact]
    public void CalculateFee_HolidaySurcharge_AppliesFiftyPercent()
    {
        var checkIn  = new DateTime(2026, 3, 16, 10, 0, 0); // Monday
        var checkOut = checkIn.AddHours(2);

        var result = _calculator.CalculateFee(VehicleType.Car, MembershipTier.Guest, checkIn, checkOut, isHoliday: true);

        Assert.Equal(3000m, result.TotalFee); // 2,000 + 1,000 holiday surcharge
    }

    [Fact]
    public void CalculateFee_HolidayOnWeekend_UsesHolidayRateOnly()
    {
        var checkIn  = new DateTime(2026, 3, 14, 10, 0, 0); // Saturday
        var checkOut = checkIn.AddHours(2);

        var result = _calculator.CalculateFee(VehicleType.Car, MembershipTier.Guest, checkIn, checkOut, isHoliday: true);

        Assert.Equal(3000m, result.TotalFee); // holiday wins — no weekend stacking
    }
    #endregion

    #region Membership Discounts
    // Test discount tiers and what amounts they apply to
    #endregion

    #region Lost Ticket
    // Test the penalty and how it interacts with other fee modifiers
    #endregion

    #region Edge Cases
    // Test invalid inputs and boundary conditions
    #endregion

    #region Property-Based Tests
    // Write at least 5 FsCheck properties that must hold for ALL valid inputs
    // You may need custom Arbitrary<T> for generating valid DateTime pairs
    #endregion
}
