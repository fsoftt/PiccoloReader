using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PiccoloReader.Core.Services;

namespace PiccoloReader.Core.ViewModels;

public partial class DonateViewModel : ObservableObject
{
    private readonly IDonateAdService _donateAdService;

    [ObservableProperty]
    private bool _isAdLoading;

    public DonateViewModel(IDonateAdService donateAdService)
    {
        _donateAdService = donateAdService;
    }

    [RelayCommand]
    private async Task SeeAdAsync()
    {
        IsAdLoading = true;
        await _donateAdService.ShowRewardedAdAsync();
        IsAdLoading = false;
    }
}
