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

        // Null kabul ediyoruz — controller KaseGerekli=true iken null gelirse
        // OrtaAlt default'una düşürüyor. Sadece dolu gelirse geçerlilik kontrol et.
        RuleFor(x => x.KaseKonum)
            .Must(k => k is null || ImzaKonumlari.IsValid(k))
            .WithMessage("Geçersiz kaşe konumu.");
    }

    private static bool BeValidSlots(List<ImzaSlotDto> slots) =>
        slots.All(s => ImzaRolleri.IsValid(s.Rol) && ImzaKonumlari.IsValid(s.Konum));
}
