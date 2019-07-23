using System.Linq;
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;

public static bool Run(ClaimsPrincipal principal)
{
    return principal.IsInRole("Developer") && principal.Identity.AuthenticationType == "aad";
}
