using System.ComponentModel.DataAnnotations;

namespace PetTemplate.Shared.Validation;

/// <summary>
/// Validates that a string field, after trimming whitespace, falls within the specified length range.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public class TrimmedStringLengthAttribute(int maximumLength) : ValidationAttribute
{
    public int MinimumLength { get; set; } = 0;

    public override bool IsValid(object? value)
    {
        if (value is not string str)
            return value is null && MinimumLength == 0;

        var trimmed = str.Trim();
        return trimmed.Length >= MinimumLength && trimmed.Length <= maximumLength;
    }

    public override string FormatErrorMessage(string name) =>
        MinimumLength > 0
            ? $"{name} must be between {MinimumLength} and {maximumLength} characters."
            : $"{name} must be at most {maximumLength} characters.";
}
