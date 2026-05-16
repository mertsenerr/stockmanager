using FluentValidation;
using SayimLink.Api.Dtos.Auth;

namespace SayimLink.Api.Validators.Auth;

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("Token zorunludur.");

        RuleFor(x => x.NewPassword).Password();
    }
}
