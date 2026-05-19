using FluentValidation;
using SayimLink.Api.Dtos.Admin;
using SayimLink.Api.Models;

namespace SayimLink.Api.Validators.Admin;

public sealed class BelgeTipiUpsertRequestValidator : AbstractValidator<BelgeTipiUpsertRequest>
{
    public BelgeTipiUpsertRequestValidator()
    {
        RuleFor(x => x.Ad)
            .NotEmpty().WithMessage("Belge tipi adı zorunludur.")
            .MaximumLength(120);

        RuleFor(x => x.Aciklama)
            .MaximumLength(1000).When(x => !string.IsNullOrEmpty(x.Aciklama));

        RuleFor(x => x.GerekenImzaRolleri)
            .NotNull()
            .Must(BeValidRoles)
            .WithMessage("Geçersiz imza rolü.");
    }

    private static bool BeValidRoles(List<string> roles) =>
        roles.All(ImzaRolleri.IsValid);
}
