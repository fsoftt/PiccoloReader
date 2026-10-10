using PiccoloReader.Core.Resources.Strings;
using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Services.Backup;
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Core.Tests.Backup;

public class BackupPlannerTests
{
    private static DriveFile Remote(string id, string key, string hash) =>
        new(id, key + ".pdf", 1, new Dictionary<string, string>
        {
            [BackupPlanner.FileProperty] = key,
            [BackupPlanner.HashProperty] = hash
        }, null);

    [Fact]
    public void Plan_ClassifiesUnchangedChangedNewMissingAndOrphans()
    {
        var same = new Sheet { Title = "Same", FileName = "same.pdf" };
        var changed = new Sheet { Title = "Changed", FileName = "changed.pdf" };
        var fresh = new Sheet { Title = "Fresh", FileName = "fresh.pdf" };
        var lost = new Sheet { Title = "Lost", FileName = "lost.pdf" };
        var hashes = new Dictionary<string, string?>
        {
            ["same.pdf"] = "H1", ["changed.pdf"] = "H2-new", ["fresh.pdf"] = "H3", ["lost.pdf"] = null
        };
        var remote = new[]
        {
            Remote("r1", "same.pdf", "h1"),
            Remote("r2", "changed.pdf", "H2-old"),
            Remote("r3", "deleted-locally.pdf", "x"),
            Remote("r4", "same.pdf", "h1")
        };

        var plan = BackupPlanner.Plan(new[] { same, changed, fresh, lost }, s => hashes[s.FileName], remote);

        Assert.Equal(1, plan.Unchanged);
        Assert.Equal(new[] { "changed.pdf", "fresh.pdf" }, plan.Uploads.Select(u => u.Sheet.FileName));
        Assert.Equal("r2", plan.Uploads[0].Existing!.Id);
        Assert.Null(plan.Uploads[1].Existing);
        Assert.Equal(new[] { "Lost" }, plan.MissingLocal);
        Assert.Equal(new[] { "r4", "r3" }, plan.RemoteOrphans.Select(o => o.Id));
    }

    [Theory]
    [InlineData("Sonata", "Sonata.pdf")]
    [InlineData("A/B\\C:D*E?F\"G<H>I|J", "A_B_C_D_E_F_G_H_I_J.pdf")]
    [InlineData("   ", "Sheet.pdf")]
    [InlineData("trailing dot.", "trailing dot.pdf")]
    public void UniquePdfName_SanitizesTitles(string title, string expected)
    {
        Assert.Equal(expected, BackupPlanner.UniquePdfName(title, new HashSet<string>()));
    }

    [Fact]
    public void UniquePdfName_AvoidsCollisionsCaseInsensitively()
    {
        var used = new HashSet<string> { "etude.pdf" };

        Assert.Equal("Etude (2).pdf", BackupPlanner.UniquePdfName("Etude", used));
        Assert.Equal("ETUDE (3).pdf", BackupPlanner.UniquePdfName("ETUDE", used));
    }

    [Fact]
    public void UniquePdfName_TruncatesVeryLongTitles()
    {
        var name = BackupPlanner.UniquePdfName(new string('a', 300), new HashSet<string>());

        Assert.Equal(104, name.Length);
    }
}

public class DriveTokenProviderTests
{
    [Fact]
    public async Task GetToken_WhenNotConnected_Throws()
    {
        var provider = new DriveTokenProvider(new FakeGoogleAuthService(), new FakeBackupStateStore { IsConnected = false });

        await Assert.ThrowsAsync<DriveAuthRequiredException>(() => provider.GetTokenAsync(default));
    }

    [Fact]
    public async Task GetToken_WhenGoogleNeedsInteraction_Throws()
    {
        var auth = new FakeGoogleAuthService { Result = new GoogleAuthResult(GoogleAuthStatus.RequiresInteraction) };
        var provider = new DriveTokenProvider(auth, new FakeBackupStateStore());

        await Assert.ThrowsAsync<DriveAuthRequiredException>(() => provider.GetTokenAsync(default));
    }

    [Fact]
    public async Task GetToken_WhenAuthorized_ReturnsToken()
    {
        var provider = new DriveTokenProvider(new FakeGoogleAuthService(), new FakeBackupStateStore());

        Assert.Equal("token", await provider.GetTokenAsync(default));
    }
}

public class BackupSettingsViewModelTests : IDisposable
{
    private readonly BackupHarness _device = new();
    private readonly FakeGoogleAuthService _auth = new();
    private readonly FakeAutoBackupScheduler _scheduler = new();
    private readonly BackupSettingsViewModel _sut;

    public BackupSettingsViewModelTests()
    {
        _device.State.IsConnected = false;
        _sut = new BackupSettingsViewModel(_device.Service, _auth, _device.Drive, _device.State, _scheduler);
    }

    public void Dispose() => _device.Dispose();

    [Fact]
    public void Initial_NotConnected_DisablesBackupActions()
    {
        Assert.False(_sut.IsConnected);
        Assert.False(_sut.CanUseBackup);
        Assert.False(_sut.AutoBackupEnabled);
    }

    [Fact]
    public async Task Connect_StoresStateAndEmail()
    {
        var message = await _sut.ConnectAsync();

        Assert.Null(message);
        Assert.True(_sut.IsConnected);
        Assert.True(_sut.CanUseBackup);
        Assert.Equal("user@example.com", _device.State.AccountEmail);
        Assert.Equal("user@example.com", _sut.AccountText);
    }

    [Fact]
    public async Task Connect_Cancelled_StaysDisconnectedWithoutMessage()
    {
        _auth.Result = new GoogleAuthResult(GoogleAuthStatus.Cancelled);

        Assert.Null(await _sut.ConnectAsync());
        Assert.False(_sut.IsConnected);
    }

    [Fact]
    public async Task Connect_Unsupported_ShowsFriendlyMessage()
    {
        _auth.IsSupported = false;

        var message = await _sut.ConnectAsync();

        Assert.NotNull(message);
        Assert.False(_sut.IsConnected);
    }

    [Fact]
    public async Task AutoBackupToggle_SchedulesAndCancelsWork()
    {
        await _sut.ConnectAsync();

        _sut.AutoBackupEnabled = true;
        Assert.True(_scheduler.Scheduled);
        Assert.True(_device.State.AutoBackupEnabled);

        _sut.AutoBackupEnabled = false;
        Assert.False(_scheduler.Scheduled);
        Assert.False(_device.State.AutoBackupEnabled);
    }

    [Fact]
    public async Task Disconnect_TurnsOffAutoBackupAndSignsOut()
    {
        await _sut.ConnectAsync();
        _sut.AutoBackupEnabled = true;

        await _sut.DisconnectAsync();

        Assert.False(_sut.IsConnected);
        Assert.False(_sut.AutoBackupEnabled);
        Assert.False(_scheduler.Scheduled);
        Assert.True(_auth.SignedOut);
        Assert.Null(_device.State.AccountEmail);
    }

    [Fact]
    public async Task BackupNow_ReportsResultAndUpdatesLastBackupText()
    {
        await _sut.ConnectAsync();
        await _device.AddSheetAsync("S", "x");

        var message = await _sut.BackupNowAsync();

        Assert.NotNull(message);
        Assert.Contains("1", message!.Text);
        Assert.NotNull(_device.State.LastBackupUtc);
        Assert.NotEqual(AppStrings.LastBackupNever, _sut.LastBackupText);
        Assert.True(_sut.IsIdle);
    }

    [Fact]
    public async Task Restore_WithoutBackup_ReturnsFriendlyError()
    {
        await _sut.ConnectAsync();

        var message = await _sut.RestoreAsync();

        Assert.Equal(AppStrings.BackupNotFoundMessage, message!.Text);
        Assert.True(_sut.IsIdle);
    }

    [Fact]
    public async Task BackupNow_WhenGoogleRefusesReauth_ReturnsAuthMessage()
    {
        await _sut.ConnectAsync();
        _device.Drive.FailListWith = new DriveAuthRequiredException("expired");
        _auth.Result = new GoogleAuthResult(GoogleAuthStatus.Cancelled);

        var message = await _sut.BackupNowAsync();

        Assert.Equal(AppStrings.BackupAuthRequiredMessage, message!.Text);
    }
}
