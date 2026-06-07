using System.ComponentModel.DataAnnotations;
using PetTemplate.Shared.Validation;
using PetTemplate.Shared.Dto;

namespace PetTemplate.Tests.Features.Validation;

public class ValidationAttributeTests
{
    // ── TrimmedStringLengthAttribute ────────────────────────────────────────

    [Fact]
    public void TrimmedStringLength_Valid_ReturnsTrue()
    {
        var attr = new TrimmedStringLengthAttribute(20) { MinimumLength = 2 };
        Assert.True(attr.IsValid("Alice"));
    }

    [Fact]
    public void TrimmedStringLength_WhitespacePadded_TrimsBeforeValidating()
    {
        var attr = new TrimmedStringLengthAttribute(20) { MinimumLength = 2 };
        Assert.True(attr.IsValid("  Alice  "));
    }

    [Fact]
    public void TrimmedStringLength_TooShortAfterTrim_ReturnsFalse()
    {
        var attr = new TrimmedStringLengthAttribute(20) { MinimumLength = 2 };
        Assert.False(attr.IsValid("a"));
    }

    [Fact]
    public void TrimmedStringLength_WhitespaceOnly_ReturnsFalse()
    {
        var attr = new TrimmedStringLengthAttribute(20) { MinimumLength = 2 };
        Assert.False(attr.IsValid("   "));
    }

    [Fact]
    public void TrimmedStringLength_TooLongAfterTrim_ReturnsFalse()
    {
        var attr = new TrimmedStringLengthAttribute(5) { MinimumLength = 2 };
        Assert.False(attr.IsValid("toolongstring"));
    }

    [Fact]
    public void TrimmedStringLength_TooLongPaddedString_TrimsFirstThenValidates()
    {
        // "  hi  " trims to "hi" (2 chars) — passes min=2, max=5
        var attr = new TrimmedStringLengthAttribute(5) { MinimumLength = 2 };
        Assert.True(attr.IsValid("  hi  "));
    }

    [Fact]
    public void TrimmedStringLength_NullWithZeroMin_ReturnsTrue()
    {
        var attr = new TrimmedStringLengthAttribute(20);
        Assert.True(attr.IsValid(null));
    }

    [Fact]
    public void TrimmedStringLength_NullWithPositiveMin_ReturnsFalse()
    {
        var attr = new TrimmedStringLengthAttribute(20) { MinimumLength = 2 };
        Assert.False(attr.IsValid(null));
    }

    [Fact]
    public void TrimmedStringLength_ExactMinLength_ReturnsTrue()
    {
        var attr = new TrimmedStringLengthAttribute(20) { MinimumLength = 2 };
        Assert.True(attr.IsValid("ab"));
    }

    [Fact]
    public void TrimmedStringLength_ExactMaxLength_ReturnsTrue()
    {
        var attr = new TrimmedStringLengthAttribute(5) { MinimumLength = 2 };
        Assert.True(attr.IsValid("abcde"));
    }

    // ProfileDto uses [TrimmedStringLength(20, MinimumLength = 2)] on UserName
    [Fact]
    public void ProfileDto_UserNameTooShort_FailsValidation()
    {
        var dto = new ProfileDto { UserName = "a" };
        var results = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(dto, new ValidationContext(dto), results, true));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(ProfileDto.UserName)));
    }

    [Fact]
    public void ProfileDto_UserNameWhitespaceOnly_FailsValidation()
    {
        var dto = new ProfileDto { UserName = "   " };
        var results = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(dto, new ValidationContext(dto), results, true));
    }

    [Fact]
    public void ProfileDto_UserNameTrimmedOnSet()
    {
        var dto = new ProfileDto { UserName = "  Alice  " };
        Assert.Equal("Alice", dto.UserName);
    }

    [Fact]
    public void ProfileDto_UserNameValid_PassesValidation()
    {
        var dto = new ProfileDto { UserName = "Alice" };
        var results = new List<ValidationResult>();
        Assert.True(Validator.TryValidateObject(dto, new ValidationContext(dto), results, true));
    }
}
