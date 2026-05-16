using FluentValidation;
using SayimLink.Api.Dtos.Sayim;
using SayimLink.Api.Models;

namespace SayimLink.Api.Validators.Sayim;

public sealed class OturumCreateRequestValidator : AbstractValidator<OturumCreateRequest>
{
    public OturumCreateRequestValidator()
    {
        RuleFor(x => x.MagazaId).NotEmpty().WithMessage("Mağaza zorunludur.");
        RuleFor(x => x.Tarih)
            .NotEmpty().WithMessage("Tarih zorunludur.")
            .Must(IsoDate).WithMessage("Tarih yyyy-MM-dd formatında olmalıdır.");
    }

    internal static bool IsoDate(string s) =>
        DateTime.TryParseExact(s, "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out _);
}

public sealed class OturumUpdateRequestValidator : AbstractValidator<OturumUpdateRequest>
{
    public OturumUpdateRequestValidator()
    {
        RuleFor(x => x.Tarih)
            .NotEmpty().WithMessage("Tarih zorunludur.")
            .Must(OturumCreateRequestValidator.IsoDate).WithMessage("Tarih yyyy-MM-dd formatında olmalıdır.");
    }
}

public sealed class OturumDurumChangeRequestValidator : AbstractValidator<OturumDurumChangeRequest>
{
    public OturumDurumChangeRequestValidator()
    {
        RuleFor(x => x.Durum)
            .NotEmpty().WithMessage("Durum zorunludur.")
            .Must(OturumDurumlari.IsValid).WithMessage("Geçersiz durum.");
    }
}

public sealed class ExcelImportRequestValidator : AbstractValidator<ExcelImportRequest>
{
    public ExcelImportRequestValidator()
    {
        RuleFor(x => x.Urunler)
            .NotEmpty().WithMessage("En az bir ürün satırı olmalı.")
            .Must(list => list.Count <= 50000)
            .WithMessage("Tek seferde en fazla 50.000 satır yüklenebilir.");

        // Per-cell length caps. Without these the JSON body can balloon past Kestrel's
        // default request limit AND drive a single SayimOturumu document past Mongo's
        // 16MB ceiling (the oturum holds the entire ürün array embedded). A 200-char
        // ürün adı × 50K rows is already ~10MB — leave headroom for the rest of the
        // document.
        RuleForEach(x => x.Urunler).ChildRules(u =>
        {
            u.RuleFor(r => r.Barkod).NotEmpty().WithMessage("Barkod zorunludur.")
                .MaximumLength(80).WithMessage("Barkod 80 karakteri geçemez.");
            u.RuleFor(r => r.UrunAdi).MaximumLength(200);
            u.RuleFor(r => r.StokKodu!).MaximumLength(80).When(r => r.StokKodu is not null);
            u.RuleFor(r => r.Kategori!).MaximumLength(100).When(r => r.Kategori is not null);
            u.RuleFor(r => r.AltKategori!).MaximumLength(100).When(r => r.AltKategori is not null);
            u.RuleFor(r => r.Renk!).MaximumLength(60).When(r => r.Renk is not null);
            u.RuleFor(r => r.Beden!).MaximumLength(40).When(r => r.Beden is not null);
            u.RuleFor(r => r.Marka!).MaximumLength(80).When(r => r.Marka is not null);
            u.RuleFor(r => r.Model!).MaximumLength(120).When(r => r.Model is not null);
        });
    }
}

public sealed class UrunPatchRequestValidator : AbstractValidator<UrunPatchRequest>
{
    public UrunPatchRequestValidator()
    {
        When(x => !string.IsNullOrEmpty(x.Durum), () =>
        {
            RuleFor(x => x.Durum!)
                .Must(UrunDurumlari.IsValid).WithMessage("Geçersiz ürün durumu.");
        });
        When(x => !string.IsNullOrEmpty(x.YorumEkle), () =>
        {
            RuleFor(x => x.YorumEkle!).MaximumLength(500).WithMessage("Yorum 500 karakteri geçemez.");
        });
        When(x => x.Barkod is not null, () =>
        {
            RuleFor(x => x.Barkod!).NotEmpty().WithMessage("Barkod boş olamaz.")
                .MaximumLength(80).WithMessage("Barkod 80 karakteri geçemez.");
        });
        When(x => x.UrunAdi is not null, () =>
        {
            RuleFor(x => x.UrunAdi!).MaximumLength(200).WithMessage("Ürün adı 200 karakteri geçemez.");
        });
    }
}
