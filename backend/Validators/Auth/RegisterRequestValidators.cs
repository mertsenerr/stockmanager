using FluentValidation;
using SayimLink.Api.Dtos.Auth;

namespace SayimLink.Api.Validators.Auth;

public sealed class RegisterSayimBaskaniRequestValidator : AbstractValidator<RegisterSayimBaskaniRequest>
{
    public RegisterSayimBaskaniRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("E-posta zorunludur.")
            .EmailAddress().WithMessage("Geçerli bir e-posta adresi girin.")
            .MaximumLength(160);

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Parola zorunludur.")
            .MinimumLength(8).WithMessage("Parola en az 8 karakter olmalıdır.")
            .MaximumLength(128);

        RuleFor(x => x.AdSoyad)
            .NotEmpty().WithMessage("Ad soyad zorunludur.")
            .MaximumLength(120);

        RuleFor(x => x.FirmaAdi)
            .NotEmpty().WithMessage("Firma adı zorunludur.")
            .MaximumLength(120);

        RuleFor(x => x.FirmaKisaltmasi)
            .NotEmpty().WithMessage("Firma kısaltması zorunludur.")
            .Matches("^[A-Za-z0-9]{3,6}$")
            .WithMessage("Firma kısaltması 3-6 karakter, sadece harf/rakam olmalıdır.");
    }
}

public sealed class RegisterKullaniciRequestValidator : AbstractValidator<RegisterKullaniciRequest>
{
    public RegisterKullaniciRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("E-posta zorunludur.")
            .EmailAddress().WithMessage("Geçerli bir e-posta adresi girin.")
            .MaximumLength(160);

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Parola zorunludur.")
            .MinimumLength(8).WithMessage("Parola en az 8 karakter olmalıdır.")
            .MaximumLength(128);

        RuleFor(x => x.AdSoyad)
            .NotEmpty().WithMessage("Ad soyad zorunludur.")
            .MaximumLength(120);

        RuleFor(x => x.FirmaKisaltmasi)
            .NotEmpty().WithMessage("Firma kısaltması zorunludur.")
            .Matches("^[A-Za-z0-9]{3,6}$")
            .WithMessage("Firma kısaltması 3-6 karakter, sadece harf/rakam olmalıdır.");
    }
}
