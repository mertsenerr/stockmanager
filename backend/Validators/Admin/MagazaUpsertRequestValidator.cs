using FluentValidation;
using SayimLink.Api.Dtos.Admin;

namespace SayimLink.Api.Validators.Admin;

public sealed class MagazaUpsertRequestValidator : AbstractValidator<MagazaUpsertRequest>
{
    public MagazaUpsertRequestValidator()
    {
        RuleFor(x => x.FirmaId).NotEmpty().WithMessage("Firma seçilmelidir.");
        RuleFor(x => x.Ad).NotEmpty().WithMessage("Mağaza adı zorunludur.")
            .MaximumLength(120).WithMessage("Mağaza adı en fazla 120 karakter olabilir.");
        RuleFor(x => x.Sehir).NotEmpty().WithMessage("Şehir zorunludur.");
        RuleFor(x => x.Ilce).NotEmpty().WithMessage("İlçe zorunludur.");
        RuleFor(x => x.Adres).NotEmpty().WithMessage("Adres zorunludur.")
            .MaximumLength(300);
        When(x => x.Koordinat is not null, () =>
        {
            RuleFor(x => x.Koordinat!.Lat).InclusiveBetween(-90, 90)
                .WithMessage("Enlem -90 ile 90 arasında olmalıdır.");
            RuleFor(x => x.Koordinat!.Lng).InclusiveBetween(-180, 180)
                .WithMessage("Boylam -180 ile 180 arasında olmalıdır.");
        });
    }
}
