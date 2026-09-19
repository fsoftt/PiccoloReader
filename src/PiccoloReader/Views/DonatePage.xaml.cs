using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Views;

public partial class DonatePage : ContentPage
{
    public DonatePage(DonateViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
