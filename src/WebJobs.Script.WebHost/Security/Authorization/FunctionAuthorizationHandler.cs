// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Description;
using static Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization.AuthUtility;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization
{
    public class FunctionAuthorizationHandler : AuthorizationHandler<FunctionAuthorizationRequirement, FunctionDescriptor>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, FunctionAuthorizationRequirement requirement, FunctionDescriptor resource)
        {
            HttpTriggerAttribute httpTrigger = resource.GetTriggerAttributeOrNull<HttpTriggerAttribute>();

            if (httpTrigger != null && PrincipalHasAuthLevelClaim(context.User, httpTrigger.AuthLevel))
            {
                if (httpTrigger.AuthLevel == AuthorizationLevel.User && httpTrigger.AllowedRoles.Length > 0)
                {
                    if (!httpTrigger.AllowedRoles
                        .Any(role => context.User.IsInRole(role)))
                    {
                        context.Fail();
                        return Task.CompletedTask;
                    }
                }
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }
}
