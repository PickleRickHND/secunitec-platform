using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using IdentitySignInResult = Microsoft.AspNetCore.Identity.SignInResult;

namespace Secunitec.Identity.Pages.Account;

// A07: lockout tras 5 intentos fallidos (Program.cs) y cada intento queda auditado con IP y correlation id.
public sealed class LoginModel : PageModel
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly SignInManager<ApplicationUser> _signIn;
    private readonly IdentityAuditWriter _audit;

    public LoginModel(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn, IdentityAuditWriter audit)
    {
        _users = users;
        _signIn = signIn;
        _audit = audit;
    }

    [BindProperty]
    public string Email { get; set; } = "";

    [BindProperty]
    public string Password { get; set; } = "";

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool Registered { get; set; }

    public string? Error { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        ApplicationUser? user = await _users.FindByEmailAsync(Email.Trim());
        IdentitySignInResult result = user is null
            ? IdentitySignInResult.Failed
            : await _signIn.PasswordSignInAsync(user, Password, isPersistent: false, lockoutOnFailure: true);

        await _audit.WriteAsync(
            HttpContext,
            result.IsLockedOut ? "login_locked_out" : "login",
            result.Succeeded,
            user?.Id,
            user?.TenantId,
            result.Succeeded ? "success" : result.IsLockedOut ? "locked_out" : "invalid_credentials",
            cancellationToken);

        if (result.Succeeded)
        {
            // A01: solo se vuelve a una URL local; Url.IsLocalUrl rechaza //host y /\host (open redirect).
            return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/");
        }

        if (result.IsLockedOut)
        {
            Response.StatusCode = StatusCodes.Status423Locked;
            Error = "La cuenta está bloqueada temporalmente por demasiados intentos fallidos.";
            return Page();
        }

        // El mismo mensaje para correo inexistente y contraseña incorrecta: no revela qué cuentas existen.
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Error = "Correo o contraseña incorrectos.";
        return Page();
    }
}
