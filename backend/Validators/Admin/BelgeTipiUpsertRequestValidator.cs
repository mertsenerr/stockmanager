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

        RuleFor(x => x.ImzaSlotlari)
            .NotNull()
            .Must(BeValidSlots)
            .WithMessage("Geçersiz imza rolü veya konumu.");

        RuleFor(x => x.KaseKonum!)
            .Must(ImzaKonumlari.IsValid)
            .WithMessage("Geçersiz kaşe konumu.")
            .When(x => x.KaseGerekli);
    }

    private static bool BeValidSlots(List<ImzaSlotDto> slots) =>
        slots.All(s => ImzaRolleri.IsValid(s.Rol) && ImzaKonumlari.IsValid(s.Konum));
}
