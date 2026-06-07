using System.ComponentModel.DataAnnotations;

namespace PetTemplate.Shared.Validation;

/// <summary>
/// Ensures that a non-zero Bet is not combined with bots (BotsCount &gt; 0).
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class NoBetWithBotsAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not { } obj) return ValidationResult.Success;

        var botsCount = (int?)obj.GetType().GetProperty("BotsCount")?.GetValue(obj) ?? 0;
        var bet = (int?)obj.GetType().GetProperty("Bet")?.GetValue(obj) ?? 0;
        var tonWager = (decimal?)obj.GetType().GetProperty("TonWager")?.GetValue(obj);

        if (botsCount > 0 && bet != 0)
            return new ValidationResult("Bet must be 0 when playing with bots.",
                [nameof(botsCount), nameof(bet)]);

        if (botsCount > 0 && tonWager > 0)
            return new ValidationResult("TonWager must be null or 0 when playing with bots.",
                [nameof(botsCount)]);

        return ValidationResult.Success;
    }
}
