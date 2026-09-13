using Greenlight.CarsClient;

namespace Greenlight.CarsClient.UnitTests;

/// <summary>
/// Which executable "Start with Windows" registers. The rule is small and the failure it
/// prevents is invisible: a versioned Velopack path works until the first update, then fails
/// at every login with nothing to connect it to the setting.
/// </summary>
public class WindowsStartupTests
{
    [Fact]
    public void A_velopack_install_registers_the_shim_beside_current_not_the_versioned_copy()
    {
        var running = @"C:\Users\jane\AppData\Local\Greenlight.CarsClient\current\Greenlight.CarsClient.exe";
        var shim = @"C:\Users\jane\AppData\Local\Greenlight.CarsClient\Greenlight.CarsClient.exe";

        Assert.Equal(shim, WindowsStartup.Launcher(running, path => path == shim));
    }

    [Fact]
    public void A_folder_merely_called_current_is_not_a_promise()
    {
        // No shim on disk, so the running path is the only thing that can be trusted to exist.
        var running = @"D:\stuff\current\Greenlight.CarsClient.exe";
        Assert.Equal(running, WindowsStartup.Launcher(running, _ => false));
    }

    [Fact]
    public void A_dev_build_or_portable_copy_is_registered_as_it_stands()
    {
        var running = @"C:\src\cars\bin\Release\net10.0-windows\Greenlight.CarsClient.exe";
        Assert.Equal(running, WindowsStartup.Launcher(running, _ => true));
    }

    [Fact]
    public void Nothing_to_register_when_the_process_path_is_unknown()
    {
        Assert.Null(WindowsStartup.Launcher(null, _ => true));
        Assert.Null(WindowsStartup.Launcher("", _ => true));
    }
}
