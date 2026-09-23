using Microsoft.AspNetCore.Identity;

namespace Secunitec.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public Guid TenantId { get; set; }
    public Guid? ClienteId { get; set; }
}
