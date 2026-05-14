using FluentValidation;
using SayimLink.Api.Dtos.Auth;

namespace SayimLink.Api.Validators.Auth;

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword)
            .NotEmpty().WithMessage("Mevcut parola zorunludur.");

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("Yeni parola zorunludur.")
            .MinimumLength(8).WithMessage("Parola en az 8 karakter olmalı.")
            .MaximumLength(128).WithMessage("Parola en fazla 128 karakter olabilir.");

        RuleFor(x => x)
            .Must(x => x.CurrentPassword != x.NewPassword)
            .WithMessage("Yeni parola mevcut parolayla aynı olamaz.")
            .When(x => !string.IsNullOrEmpty(x.NewPassword) && !string.IsNullOrEmpty(x.CurrentPassword));
    }
}
