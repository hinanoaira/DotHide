using System;
using DotHide.Startup;
using Xunit;

namespace DotHide.Tests;

public class StartupManagerTests
{
    [Fact]
    public void StartupManager_CanCheckStartupStatus()
    {
        // Testing that calling IsStartupEnabled does not throw any exceptions
        var exception = Record.Exception(() => StartupManager.IsStartupEnabled());
        Assert.Null(exception);
    }
}

