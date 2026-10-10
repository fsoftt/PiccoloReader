using PiccoloReader.Core.Data;
using PiccoloReader.Core.Services;
using PiccoloReader.Core.ViewModels;
using SQLitePCL;

namespace PiccoloReader.Core.Tests.Services;

public class PageCropServiceTests : IDisposable
{
    private readonly TestAppStorageProvider _storage = new();
    private readonly PageCropService _sut;

    public PageCropServiceTests()
    {
        Batteries_V2.Init();
        var database = new AppDatabase(_storage.DatabasePath);
        database.InitializeAsync().GetAwaiter().GetResult();
        _sut = new PageCropService(database);
    }

    public void Dispose() => _storage.Dispose();

    [Fact]
    public async Task SetCrop_ThenGet_ReturnsItPerPage()
    {
        var crop = new PageCrop(0.1, 0.2, 0.9, 0.8);
        await _sut.SetCropAsync(1, 2, crop);

        var crops = await _sut.GetCropsAsync(1);

        Assert.Equal(crop, crops[2]);
        Assert.Single(crops);
        Assert.Empty(await _sut.GetCropsAsync(2));
    }

    [Fact]
    public async Task SetCrop_Twice_ReplacesTheCrop()
    {
        await _sut.SetCropAsync(1, 0, new PageCrop(0.1, 0.1, 0.9, 0.9));
        await _sut.SetCropAsync(1, 0, new PageCrop(0.2, 0.2, 0.8, 0.8));

        var crops = await _sut.GetCropsAsync(1);

        Assert.Single(crops);
        Assert.Equal(new PageCrop(0.2, 0.2, 0.8, 0.8), crops[0]);
    }

    [Fact]
    public async Task SetCrop_Full_RemovesTheCrop()
    {
        await _sut.SetCropAsync(1, 0, new PageCrop(0.1, 0.1, 0.9, 0.9));
        await _sut.SetCropAsync(1, 0, PageCrop.Full);

        Assert.Empty(await _sut.GetCropsAsync(1));
    }

    [Fact]
    public async Task SetCrop_Invalid_IsNotStored()
    {
        await _sut.SetCropAsync(1, 0, new PageCrop(0.5, 0.5, 0.55, 0.55));

        Assert.Empty(await _sut.GetCropsAsync(1));
    }
}
