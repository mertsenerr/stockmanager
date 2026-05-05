using FluentValidation;
using SayimLink.Api.Dtos.Takvim;
using SayimLink.Api.Models;

namespace SayimLink.Api.Validators.Takvim;

public sealed class AtamaUpsertRequestValidator : AbstractValidator<AtamaUpsertRequest>
{
    public AtamaUpsertRequestValidator()
    {
        RuleFor(x => x.MagazaId).NotEmpty().WithMessage("Mağaza zorunludur.");
        RuleFor(x => x.YoneticiKullaniciId).NotEmpty().WithMessage("Sayım yöneticisi zorunludur.");
        RuleFor(x => x.Tarih)
            .NotEmpty().WithMessage("Tarih zorunludur.")
            .Must(BeIsoDate).WithMessage("Tarih yyyy-MM-dd formatında olmalıdır.");

        RuleFor(x => x.BaslangicSaati)
            .Must(BeHourMinuteOrEmpty).WithMessage("Başlangıç saati HH:mm formatında olmalıdır.");
        RuleFor(x => x.BitisSaati)
            .Must(BeHourMinuteOrEmpty).WithMessage("Bitiş saati HH:mm formatında olmalıdır.");

        RuleFor(x => x.Durum)
            .Must(AtamaDurumlari.IsValid).WithMessage("Geçersiz durum.");

        RuleFor(x => x.SaymanKullaniciIds)
            .Must((req, ids) => !ids.Contains(req.YoneticiKullaniciId))
            .WithMessage("Sayım yöneticisi aynı zamanda sayman olarak eklenemez.");
    }

    private static bool BeIsoDate(string s) =>
        DateTime.TryParseExact(s, "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out _);

    private static bool BeHourMinuteOrEmpty(string? s)
    {
        if (string.IsNullOrEmpty(s)) return true;
        return TimeOnly.TryParseExact(s, "HH:mm",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out _);
    }
}

public sealed class AtamaTarihUpdateRequestValidator : AbstractValidator<AtamaTarihUpdateRequest>
{
    public AtamaTarihUpdateRequestValidator()
    {
        RuleFor(x => x.Tarih)
            .NotEmpty().WithMessage("Tarih zorunludur.")
            .Must(s => DateTime.TryParseExact(s, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out _))
            .WithMessage("Tarih yyyy-MM-dd formatında olmalıdır.");
    }
}
