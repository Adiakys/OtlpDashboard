using System.ComponentModel.DataAnnotations;
using OpenTelemetryDashboard.Persistence.Retention;

namespace OpenTelemetryDashboard.UnitTests.Retention;

public sealed class TelemetryLimitsOptionsTests
{
    [Fact]
    public void Defaults_Are_Valid_And_Retention_Is_Disabled()
    {
        var options = new TelemetryLimitsOptions();

        Validate(options).ShouldBeEmpty();
        options.MaxLogDays.ShouldBe(0.0);
        options.MaxTraceDays.ShouldBe(0.0);
        options.MaxMetricDays.ShouldBe(0.0);
        options.SweepIntervalMinutes.ShouldBe(60);
    }

    [Fact]
    public void Zero_Days_Is_Accepted_As_Disabled()
    {
        var options = new TelemetryLimitsOptions
        {
            MaxLogDays = 0,
            MaxTraceDays = 0,
            MaxMetricDays = 0,
        };

        Validate(options).ShouldBeEmpty();
    }

    [Fact]
    public void Fractional_Days_Are_Accepted()
    {
        // Metrics in-memory typically want sub-day retention (e.g., 30 minutes ≈ 0.021 days).
        var options = new TelemetryLimitsOptions
        {
            MaxMetricDays = 0.5,
            MaxLogDays = 1.25,
        };

        Validate(options).ShouldBeEmpty();
    }

    [Fact]
    public void Negative_Days_Are_Rejected()
    {
        var options = new TelemetryLimitsOptions { MaxLogDays = -1 };

        Validate(options).ShouldNotBeEmpty();
    }

    [Fact]
    public void Values_Beyond_Max_Are_Rejected()
    {
        var options = new TelemetryLimitsOptions { MaxTraceDays = 4000 };

        Validate(options).ShouldNotBeEmpty();
    }

    [Fact]
    public void SweepIntervalMinutes_Must_Be_At_Least_One()
    {
        var options = new TelemetryLimitsOptions { SweepIntervalMinutes = 0 };

        Validate(options).ShouldNotBeEmpty();
    }

    private static List<ValidationResult> Validate(TelemetryLimitsOptions options)
    {
        var context = new ValidationContext(options);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, context, results, validateAllProperties: true);
        return results;
    }
}
