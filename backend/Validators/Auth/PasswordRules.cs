using FluentValidation;

namespace SayimLink.Api.Validators.Auth;

/// <summary>Single source of truth for password complexity. Apply with
/// <c>RuleFor(x =&gt; x.Password).Password();</c> on any validator so reset/
/// register/change can't drift apart.</summary>
public static class PasswordRules
{
    public static IRuleBuilderOptions<T, string> Password<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("Parola zorunludur.")
            .MinimumLength(8).WithMessage("Parola en az 8 karakter olmalıdır.")
            .MaximumLength(128).WithMessage("Parola en fazla 128 karakter olabilir.")
            .Matches("[A-Za-z]").WithMessage("Parola en az bir harf içermelidir.")
            .Matches("[0-9]").WithMessage("Parola en az bir rakam içermelidir.");
}
