using FluentValidation;
using SayimLink.Api.Dtos.Auth;

namespace SayimLink.Api.Validators.Auth;

public sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(x => x.AdSoyad)
            .NotEmpty().WithMessage("Ad soyad zorunludur.")
            .MinimumLength(2).WithMessage("Ad soyad en az 2 karakter olmalı.")
            .MaximumLength(120).WithMessage("Ad soyad en fazla 120 karakter olabilir.");
    }
}
