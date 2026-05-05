using FluentValidation;
using SayimLink.Api.Dtos.Admin;
using SayimLink.Api.Models;

namespace SayimLink.Api.Validators.Admin;

public sealed class KullaniciCreateRequestValidator : AbstractValidator<KullaniciCreateRequest>
{
    public KullaniciCreateRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email zorunludur.")
            .EmailAddress().WithMessage("Geçerli bir email adresi giriniz.");

        RuleFor(x => x.AdSoyad)
            .NotEmpty().WithMessage("Ad Soyad zorunludur.")
            .MaximumLength(120);

        RuleFor(x => x.Rol)
            .NotEmpty().WithMessage("Rol zorunludur.")
            .Must(Roles.IsValid).WithMessage("Geçersiz rol.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Parola zorunludur.")
            .MinimumLength(8).WithMessage("Parola en az 8 karakter olmalıdır.")
            .Matches("[A-Za-z]").WithMessage("Parola en az bir harf içermelidir.")
            .Matches("[0-9]").WithMessage("Parola en az bir rakam içermelidir.");
    }
}

public sealed class KullaniciUpdateRequestValidator : AbstractValidator<KullaniciUpdateRequest>
{
    public KullaniciUpdateRequestValidator()
    {
        RuleFor(x => x.AdSoyad).NotEmpty().WithMessage("Ad Soyad zorunludur.")
            .MaximumLength(120);
        RuleFor(x => x.Rol).NotEmpty().WithMessage("Rol zorunludur.")
            .Must(Roles.IsValid).WithMessage("Geçersiz rol.");

        When(x => !string.IsNullOrEmpty(x.NewPassword), () =>
        {
            RuleFor(x => x.NewPassword!)
                .MinimumLength(8).WithMessage("Parola en az 8 karakter olmalıdır.")
                .Matches("[A-Za-z]").WithMessage("Parola en az bir harf içermelidir.")
                .Matches("[0-9]").WithMessage("Parola en az bir rakam içermelidir.");
        });
    }
}
