using FluentValidation;
using SayimLink.Api.Dtos.Admin;
using SayimLink.Api.Models;

namespace SayimLink.Api.Validators.Admin;

public sealed class FirmaUpsertRequestValidator : AbstractValidator<FirmaUpsertRequest>
{
    public FirmaUpsertRequestValidator()
    {
        RuleFor(x => x.Ad)
            .NotEmpty().WithMessage("Firma adı zorunludur.")
            .MaximumLength(120).WithMessage("Firma adı en fazla 120 karakter olabilir.");

        RuleFor(x => x.Tip)
            .NotEmpty().WithMessage("Firma tipi zorunludur.")
            .Must(FirmaTipleri.IsValid)
            .WithMessage("Firma tipi geçersiz.");

        RuleFor(x => x.LogoUrl)
            .Must(BeValidHttpUrl)
            .When(x => !string.IsNullOrWhiteSpace(x.LogoUrl))
            .WithMessage("Logo URL geçerli bir http(s) adresi olmalıdır.");

        RuleFor(x => x.Kisaltma!)
            .Matches("^[A-Za-z0-9]{3,6}$")
            .When(x => !string.IsNullOrWhiteSpace(x.Kisaltma))
            .WithMessage("Firma kısaltması 3-6 karakter, sadece harf/rakam olmalıdır.");
    }

    private static bool BeValidHttpUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
