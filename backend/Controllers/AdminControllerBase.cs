using System.Security.Claims;
using FluentValidation.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SayimLink.Api.Models;

namespace SayimLink.Api.Controllers;

[ApiController]
[Authorize(Roles = Roles.Sistem + "," + Roles.SayimBaskani)]
public abstract class AdminControllerBase : ControllerBase
{
    protected string? CurrentUserId =>
        User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    protected IActionResult ValidationFailure(ValidationResult result)
    {
        var errors = result.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
        return ValidationProblem(new ValidationProblemDetails(errors));
    }
}
