﻿using System;

namespace DesktopClock.Tests;

public class DateTimeTests
{
    [Theory]
    [InlineData("2024-07-15T00:00:00Z", "00:00:00")]
    [InlineData("2024-07-15T00:00:00Z", "01:00:00")]
    [InlineData("0001-01-01T00:00:00Z", "00:00:00")]
    public void ToDateTimeOffset_ShouldConvertDateTimeToExpectedOffset(string dateTimeString, string offsetString)
    {
        // Arrange
        var dateTime = DateTime.Parse(dateTimeString);
        var offset = TimeSpan.Parse(offsetString);

        // Act
        var dateTimeOffset = dateTime.ToDateTimeOffset(offset);

        // Assert
        Assert.Equal(new DateTimeOffset(dateTime.Ticks, offset), dateTimeOffset);
    }

    [Theory]
    [InlineData("2024-07-18T12:30:45.123Z", "2024-07-18T12:30:45.456Z", true)] // Different millisecond
    [InlineData("2024-07-18T12:30:45.123Z", "2024-07-18T12:30:46.123Z", false)] // Different second
    [InlineData("2024-07-18T12:30:45.123Z", "2024-07-18T12:31:45.123Z", false)] // Different minute
    [InlineData("2024-07-18T12:30:45.123Z", "2024-07-18T13:30:45.123Z", false)] // Different hour
    [InlineData("2024-07-18T12:30:45.123Z", "2024-07-19T12:30:45.123Z", false)] // Different day
    [InlineData("2024-07-18T12:30:45.123Z", "2024-08-18T12:30:45.123Z", false)] // Different month
    [InlineData("2024-07-18T12:30:45.123Z", "2025-07-18T12:30:45.123Z", false)] // Different year
    public void AreEqualExcludingMilliseconds(string dt1String, string dt2String, bool expected)
    {
        // Arrange
        var dt1 = DateTimeOffset.Parse(dt1String);
        var dt2 = DateTimeOffset.Parse(dt2String);

        // Act
        var result = dt1.AreEqualExcludingMilliseconds(dt2);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("2024-07-18T12:30:00Z", "2024-07-18T12:30:00Z", 60, true)] // Countdown reached
    [InlineData("2024-07-18T12:29:00Z", "2024-07-18T12:30:00Z", 60, false)] // Not yet reached, not on interval
    [InlineData("2024-07-18T12:30:30Z", "2024-07-18T12:30:01Z", 30, true)] // On interval
    [InlineData("2024-07-18T12:30:01Z", "2024-07-18T12:30:00Z", 30, true)] // Countdown on interval
    public void IsOnInterval(string dtString, string countdownToString, int intervalSeconds, bool expected)
    {
        // Arrange
        var dateTime = DateTimeOffset.Parse(dtString);
        var countdownTo = DateTimeOffset.Parse(countdownToString);
        var interval = TimeSpan.FromSeconds(intervalSeconds);

        // Act
        var result = DateTimeUtil.IsOnInterval(dateTime, countdownTo, interval);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("dddd, MMMM dd", "Monday, January 01")]
    [InlineData("yyyy-MM-dd", "2024-01-01")]
    [InlineData("HH:mm:ss", "00:00:00")]
    [InlineData("MMMM dd, yyyy", "January 01, 2024")]
    public void FromFormat_CreatesCorrectExample(string format, string expected)
    {
        // Arrange
        var dateTimeOffset = new DateTime(2024, 01, 01);

        // Act
        var dateFormatExample = DateFormatExample.FromFormat(format, dateTimeOffset);

        // Assert
        Assert.Equal(format, dateFormatExample.Format);
        Assert.Equal(expected, dateFormatExample.Example);
    }
}
