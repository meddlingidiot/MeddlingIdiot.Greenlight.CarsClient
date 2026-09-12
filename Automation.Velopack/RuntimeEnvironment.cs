using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Automation.Velopack;

[ExcludeFromCodeCoverage]
public static class RuntimeEnvironment
{
    public static bool IsAdministrator
    {
        get
        {
#if NET5_0_OR_GREATER
            if (!OperatingSystem.IsWindows())
                return false;
#else
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return false;
#endif

#if RELEASE
            bool isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent())
                .IsInRole(WindowsBuiltInRole.Administrator);
#else //Debug
            bool isAdmin = true;
#endif            
            return isAdmin;
        }
    }

}