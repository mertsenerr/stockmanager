using FluentValidation;
using SayimLink.Api.Dtos.Admin;

namespace SayimLink.Api.Validators.Admin;

public sealed class OzelRaporUpsertRequestValidator : AbstractValidator<OzelRaporUpsertRequest>
{
    public OzelRaporUpsertRequestValidator()
    {
        RuleFor(x => x.Ad)
            .NotEmpty().WithMessage("Rapor adı zorunludur.")
            .MaximumLength(160);

        RuleFor(x => x.Aciklama)
            .MaximumLength(2000).When(x => !string.IsNullOrEmpty(x.Aciklama));

        RuleFor(x => x.ErisebilenKullaniciIds)
            .NotNull();
    }
}
