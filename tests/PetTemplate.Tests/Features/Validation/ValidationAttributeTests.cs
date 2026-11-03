using System.ComponentModel.DataAnnotations;
using PetTemplate.Shared.Validation;
using PetTemplate.Shared.Dto;
using PetTemplate.Shared.Errors;

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

    // ── Правила аккаунта (спец. accounts, 4.1 и 8.1) ───────────────────────

    [Theory]
    [InlineData("Oleg", true)]
    [InlineData("oleg.petrov_2", true)]
    [InlineData("ol", false)]
    [InlineData("Olezhka_Petrovich_123", false)]
    [InlineData("1oleg", false)]
    [InlineData("Олег", false)]
    [InlineData("tg123", false)]
    [InlineData("Vk42", false)]
    [InlineData("tg123_2", true)]
    public void UserNameRule_AllowsOnlyWhatPersonMayChoose(string userName, bool allowed)
    {
        Assert.Equal(allowed, UserNameRule.IsAllowedForPerson(userName));
    }

    /// <summary>Автоматический логин укладывается в правила формата сам (4.2)</summary>
    [Fact]
    public void UserNameRule_AutomaticFitsFormat()
    {
        Assert.True(UserNameRule.HasValidFormat(UserNameRule.ForTelegram(9_999_999_999)));
        Assert.True(UserNameRule.HasValidFormat(UserNameRule.ForVk(9_999_999_999) + "_2"));
    }

    [Fact]
    public void RegisterDto_ReportsCodesForRules()
    {
        var dto = new RegisterDto
        {
            UserName = "tg42",
            Phone = "8 999 000-00-01",
            Email = "not-an-email",
            Password = "short",
            PasswordConfirm = "short"
        };
        var results = new List<ValidationResult>();

        Assert.False(Validator.TryValidateObject(dto, new ValidationContext(dto), results, true));
        Assert.Contains(results, r => r.ErrorMessage == ValidationCodes.UserNameReserved);
        Assert.Contains(results, r => r.ErrorMessage == ValidationCodes.PhoneFormat);
        Assert.Contains(results, r => r.ErrorMessage!.StartsWith(ValidationCodes.StringLength));
    }

    [Fact]
    public void ValidationError_ParsesPackedCodeAndPlainText()
    {
        var packed = ValidationError.Parse(ValidationError.Pack(ValidationCodes.StringLength, ("min", 8), ("max", 100)));
        var plain = ValidationError.Parse("The Login field is required.");

        Assert.Equal(ValidationCodes.StringLength, packed.Code);
        Assert.Equal(2, packed.Params!.Count);
        Assert.Null(plain.Code);
        Assert.Equal("The Login field is required.", plain.Text);
    }
}
