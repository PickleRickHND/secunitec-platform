using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Secunitec.Identity.Pages.Account;

// El usuario autoregistrado queda sin rol: Cliente exige un cliente_id que asigna un Admin, y sin rol Billing le
// niega todo (R04). El registro no revela si un correo ya existe (A07).
public sealed class RegisterModel : PageModel
{
    private static readonly string[] _duplicateCodes =
        [nameof(IdentityErrorDescriber.DuplicateEmail), nameof(IdentityErrorDescriber.DuplicateUserName)];

    private readonly UserManager<ApplicationUser> _users;
    private readonly IConfiguration _configuration;
    private readonly IdentityAuditWriter _audit;

    public RegisterModel(UserManager<ApplicationUser> users, IConfiguration configuration, IdentityAuditWriter audit)
    {
        _users = users;
        _configuration = configuration;
        _audit = audit;
    }

    [BindProperty]
    public string Email { get; set; } = "";

    [BindProperty]
    public string Password { get; set; } = "";

    public IReadOnlyList<string> Errors { get; private set; } = [];

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        Guid tenantId = IdentitySeeder.RequiredGuid(_configuration, "Identity:RegistrationTenantId");
        string email = Email.Trim();
        ApplicationUser user = new() { Id = Guid.NewGuid(), UserName = email, Email = email, TenantId = tenantId };

        IdentityResult result = await _users.CreateAsync(user, Password);
        await _audit.WriteAsync(
            HttpContext,
            "register",
            result.Succeeded,
            result.Succeeded ? user.Id : null,
            tenantId,
            result.Succeeded ? "created" : string.Join(";", result.Errors.Select(error => error.Code)),
            cancellationToken);

        // Un correo ya registrado responde igual que un alta exitosa. Los errores de formato o de política de
        // contraseña sí se muestran: saldrían igual con un correo nuevo.
        IdentityError[] visible = result.Errors.Where(error => !_duplicateCodes.Contains(error.Code)).ToArray();
        if (result.Succeeded || visible.Length == 0)
        {
            return RedirectToPage("/Account/Login", new { registered = true });
        }

        Errors = visible.Select(error => error.Description).ToArray();
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return Page();
    }
}
