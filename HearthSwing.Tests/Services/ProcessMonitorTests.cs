using System.Diagnostics;
using AutoFixture;
using AutoFixture.AutoNSubstitute;
using HearthSwing.Services;
using NSubstitute;
using Shouldly;

namespace HearthSwing.Tests.Services;

[TestFixture]
public class ProcessMonitorTests
{
    private IFixture _fixture = null!;
    private IProcessManager _processManager = null!;
    private IFileSystem _fs = null!;
    private ProcessMonitor _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoNSubstituteCustomization());
        _processManager = _fixture.Freeze<IProcessManager>();
        _fs = _fixture.Freeze<IFileSystem>();

        _processManager.GetProcessesByName(Arg.Any<string>()).Returns(Array.Empty<Process>());

        _sut = new ProcessMonitor(_processManager, _fs);
    }

    [Test]
    public void IsWowRunning_WhenProcessExists_ReturnsTrue()
    {
        // Arrange
        var mockProcess = Substitute.For<Process>();
        _processManager.GetProcessesByName("WowClassic").Returns(new[] { mockProcess });

        // Act
        var result = _sut.IsWowRunning();

        // Assert
        result.ShouldBeTrue();
    }

    [Test]
    public void IsWowRunning_WhenNoProcess_ReturnsFalse()
    {
        // Act
        var result = _sut.IsWowRunning();

        // Assert
        result.ShouldBeFalse();
    }

    [Test]
    public void LaunchWow_WhenExeNotFound_ThrowsFileNotFoundException()
    {
        // Arrange
        _fs.FileExists(Arg.Any<string>()).Returns(false);

        // Act & Assert
        Should.Throw<FileNotFoundException>(() => _sut.LaunchWow(@"C:\Game"));
    }

    [Test]
    public void LaunchWow_WhenExeExists_StartsProcess()
    {
        // Arrange
        _fs.FileExists(@"C:\Game\WowClassic.exe").Returns(true);

        // Act
        _sut.LaunchWow(@"C:\Game");

        // Assert
        _processManager
            .Received()
            .Start(
                Arg.Is<ProcessStartInfo>(psi =>
                    psi.FileName == @"C:\Game\WowClassic.exe"
                    && psi.WorkingDirectory == @"C:\Game"
                    && psi.UseShellExecute
                )
            );
    }

    [Test]
    public void LaunchWow_WhenExeExists_FiresLogEvent()
    {
        // Arrange
        _fs.FileExists(@"C:\Game\WowClassic.exe").Returns(true);
        var logMessages = new List<string>();
        _sut.Log += msg => logMessages.Add(msg);

        // Act
        _sut.LaunchWow(@"C:\Game");

        // Assert
        logMessages.ShouldContain(m => m.Contains("Launching"));
    }

    [Test]
    public async Task WaitForExitAsync_WhenNoProcess_ReturnsImmediately()
    {
        // Act
        await _sut.WaitForExitAsync(CancellationToken.None);

        // Assert
        _processManager.Received(1).GetProcessesByName("WowClassic");
    }

    [Test]
    public async Task WaitForExitAsync_WhenCancelled_StopsPolling()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert — should not throw, just exit
        await _sut.WaitForExitAsync(cts.Token);
    }
}
