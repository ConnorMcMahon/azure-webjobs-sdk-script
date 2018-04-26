#r "bin/Microsoft.Xrm.Sdk.dll"

using System;
using Microsoft.Xrm.Sdk;

public static void Run(IServiceProvider serviceProvider)
{
    ITracingService trace = serviceProvider.GetService(typeof(ITracingService)) as ITracingService;

    IOrganizationServiceFactory orgFactory = serviceProvider.GetService(typeof(IOrganizationServiceFactory)) as IOrganizationServiceFactory;
    IOrganizationService orgService = orgFactory.CreateOrganizationService(null);
    string msg = $"[{DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff")}]:  ****************** It works!!!";
}