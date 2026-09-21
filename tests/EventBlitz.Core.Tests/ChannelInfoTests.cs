using EventBlitz.Core.Models;
using Xunit;

namespace EventBlitz.Core.Tests;

public class ChannelInfoTests
{
    [Theory]
    [InlineData("Application", ChannelInfo.WindowsLogsGroup, "", "Application")]
    [InlineData("Security", ChannelInfo.WindowsLogsGroup, "", "Security")]
    [InlineData("Microsoft-Windows-Kernel-Power/Thermal-Operational", ChannelInfo.ServicesLogsGroup, "Microsoft / Windows", "Kernel-Power / Thermal-Operational")]
    [InlineData("Microsoft-Windows-PowerShell/Operational", ChannelInfo.ServicesLogsGroup, "Microsoft / Windows", "PowerShell / Operational")]
    [InlineData("Windows PowerShell", ChannelInfo.ServicesLogsGroup, "", "Windows PowerShell")]
    [InlineData("OpenSSH/Admin", ChannelInfo.ServicesLogsGroup, "", "OpenSSH / Admin")]
    [InlineData("Microsoft-ServerManagementExperience", ChannelInfo.ServicesLogsGroup, "Microsoft", "ServerManagementExperience")]
    public void Describe_nests_channels_like_the_built_in_viewer(string name, string group, string folder, string display)
    {
        var (g, f, d) = ChannelInfo.Describe(name);
        Assert.Equal(group, g);
        Assert.Equal(folder, f);
        Assert.Equal(display, d);
    }
}
