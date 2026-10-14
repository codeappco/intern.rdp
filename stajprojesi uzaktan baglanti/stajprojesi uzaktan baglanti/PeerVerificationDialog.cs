using System.Windows;
using System.Windows.Controls;

namespace stajprojesi_uzaktan_baglanti;

public sealed class PeerVerificationDialog : Window
{
    public PeerVerificationDialog(Window owner, string code, PeerVerification verification)
    {
        Owner = owner;
        Title = "Karsi bilgisayarin anahtarini dogrula";
        Width = 620; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = PeerVerification.Instructions, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "Bu bilgisayarin kodu (karsi tarafa okuyun):", Margin = new Thickness(0, 16, 0, 6) });
        panel.Children.Add(new TextBox { Text = code, IsReadOnly = true, FontSize = 18 });
        panel.Children.Add(new TextBlock { Text = "Bagimsiz kanaldan aldiginiz KARSI bilgisayarin kodu:", Margin = new Thickness(0, 16, 0, 6) });
        var entered = new TextBox { MaxLength = 64, FontSize = 18 };
        panel.Children.Add(entered);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        var cancel = new Button { Content = "Iptal - oturumu kapat", IsCancel = true, Padding = new Thickness(12, 8, 12, 8) };
        var confirm = new Button { Content = "Kodu karsilastir", IsDefault = false, Margin = new Thickness(10, 0, 0, 0), Padding = new Thickness(12, 8, 12, 8) };
        confirm.Click += (_, _) => { DialogResult = verification.ConfirmPeerCode(entered.Text); };
        buttons.Children.Add(cancel); buttons.Children.Add(confirm); panel.Children.Add(buttons);
        Content = panel;
        Loaded += (_, _) => cancel.Focus();
        Closed += (_, _) => { if (DialogResult != true) verification.Close(); };
    }
}
