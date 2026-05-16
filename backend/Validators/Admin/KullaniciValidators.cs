using FluentValidation;
using SayimLink.Api.Dtos.Admin;
using SayimLink.Api.Models;
using SayimLink.Api.Validators.Auth;

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

        RuleFor(x => x.Password).Password();
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
            RuleFor(x => x.NewPassword!).Password();
        });
    }
}
